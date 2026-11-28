"""Verify the packaged offline migration command against fresh and legacy SQLite files."""

import argparse
from contextlib import closing
from hashlib import sha256
import json
import os
from pathlib import Path
from shutil import copyfile
import sqlite3
import subprocess
from tempfile import TemporaryDirectory
from uuid import uuid4

from foundation_smoke import start_app, wait_for_server


MIGRATIONS = Path(__file__).resolve().parents[1] / "src" / "MangaOrganizer.Server" / "Migrations"


def run_migrations(app, data, expected=0):
    env = dict(os.environ, MANGA_ORGANIZER_DATA_DIR=str(data), ASPNETCORE_URLS="http://0.0.0.0:0")
    env.pop("MANGA_ORGANIZER_BOOTSTRAP_PASSWORD", None)
    result = subprocess.run([app, "--migrate"], env=env, text=True, capture_output=True, timeout=30)
    assert result.returncode == expected, (result.returncode, result.stdout, result.stderr)
    return result


def entries(data):
    with closing(sqlite3.connect(data / "organizer.sqlite")) as database:
        return database.execute("SELECT version, name, applied_at FROM schema_migration ORDER BY version").fetchall()

def verified_snapshot(data, source_schema, target_schema):
    (backup,) = (data / "upgrade-backups").iterdir()
    manifest = json.loads((backup / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["SourceSchema"] == source_schema and manifest["TargetSchema"] == target_schema
    for item in manifest["Files"]:
        content = (backup / item["Path"]).read_bytes()
        assert len(content) == item["Bytes"] and sha256(content).hexdigest() == item["Sha256"]
    with closing(sqlite3.connect(backup / "organizer.sqlite")) as database:
        assert database.execute("PRAGMA integrity_check").fetchone() == ("ok",)
    return backup


def verify_canonical_upgrade(app, fresh, data, target_version):
    data.mkdir()
    root = str(uuid4())
    with (closing(sqlite3.connect(fresh / "organizer.sqlite")) as source,
          closing(sqlite3.connect(data / "organizer.sqlite")) as database):
        source.backup(database)
        database.executescript("""
            DROP TABLE library_scan_metadata;
            DROP TABLE library_scan_checkpoint;
            DROP TABLE library_sidecar_stamp;
            ALTER TABLE library_file DROP COLUMN scan_stamp;
            ALTER TABLE library_file DROP COLUMN scan_chapters;
            ALTER TABLE library_file DROP COLUMN scan_chapter_source;
            ALTER TABLE library_root DROP COLUMN last_scan;
            DROP TABLE series_release_metadata;
            DROP TABLE series_identity_alias;
            DROP TABLE series_cover_preference;
            DROP INDEX series_metadata_anilist_id;
            DROP INDEX content_metadata_series;
            ALTER TABLE series_metadata DROP COLUMN manual_tags;
            DELETE FROM schema_migration WHERE version > 23;
            INSERT INTO series_metadata(id, series_key, title, kind, provider_id, updated_at)
              VALUES ('first', 'alpha', 'Saved custom title', 'manga', 321, '2026-01-01'),
                     ('second', 'beta', 'Older duplicate title', 'manga', 321, '2026-01-01'),
                     ('local', 'gamma', 'Independent manual work', 'manga', NULL, '2026-01-01');
            INSERT INTO library_content(id, sha256) VALUES ('release', zeroblob(32)), ('bound', randomblob(32));
            INSERT INTO content_metadata VALUES ('release', 'second'), ('bound', 'second');
            INSERT INTO chapter_completion VALUES ('owner', 'second', '2', '8', 1);
            INSERT INTO edition_volume_manifest VALUES ('edition', 'second', 'Verified edition', '2', '["8"]');
            """)
        database.execute("INSERT INTO library_root (id, path_key) VALUES (?, ?)", (root, str(data)))
        database.execute("INSERT INTO library_folder (root_id, path, kind) VALUES (?, ?, 'both')", (root, str(data)))
        database.execute("INSERT INTO series_metadata_binding VALUES (?, 'other-bound-key', 'second')", (root,))
        database.execute("""
            INSERT INTO library_file (root_id, relative_path, extension, size, modified_at, series_key, content_id)
            VALUES (?, 'Unbound release.cbz', 'cbz', 1, '2026-01-01', 'beta', 'release')
            """, (root,))
        database.commit()

    run_migrations(app, data)
    snapshot = verified_snapshot(data, 23, target_version)
    with closing(sqlite3.connect(snapshot / "organizer.sqlite")) as backup:
        assert backup.execute("SELECT COUNT(*) FROM series_metadata WHERE provider_id = 321").fetchone() == (2,)
    with closing(sqlite3.connect(data / "organizer.sqlite")) as database:
        assert database.execute("PRAGMA foreign_key_check").fetchall() == []
        assert database.execute("SELECT id, title, provider_id FROM series_metadata ORDER BY id").fetchall() == [
            ("first", "Saved custom title", 321), ("local", "Independent manual work", None)
        ]
        assert database.execute("SELECT * FROM series_identity_alias").fetchall() == [("second", "first")]
        assert database.execute("SELECT series_key, metadata_id FROM series_metadata_binding ORDER BY series_key").fetchall() == [
            ("beta", "first"), ("other-bound-key", "first")
        ]
        assert database.execute("SELECT DISTINCT metadata_id FROM content_metadata").fetchall() == [("first",)]
        history = database.execute("""
            SELECT alias.series_id, status.completed, edition.chapters
            FROM series_identity_alias alias JOIN chapter_completion status ON status.work_key = alias.previous_id
            JOIN edition_volume_manifest edition ON edition.work_key = alias.previous_id
            """).fetchone()
        assert history == ("first", 1, '["8"]')
        try:
            database.execute("""
                INSERT INTO series_metadata(id, series_key, title, kind, provider_id, updated_at)
                VALUES ('duplicate', 'delta', 'Blocked duplicate', 'manga', 321, '2026-01-01')
                """)
        except sqlite3.IntegrityError:
            pass
        else:
            raise AssertionError("AniList ID uniqueness was not enforced")


def verify(app, fixture, previous_app=None):
    fresh = fixture / "fresh"
    run_migrations(app, fresh)
    installed = entries(fresh)
    assert [row[1] for row in installed] == [script.stem for script in sorted(MIGRATIONS.glob("*.sql"))]
    run_migrations(app, fresh)
    assert entries(fresh) == installed
    assert not (fresh / "upgrade-backups").exists()
    verify_canonical_upgrade(app, fresh, fixture / "pre-canonical", len(installed))

    # Exercise the exact schema from the previous managed-file preview with an AniList assignment.
    keys = fresh / "keys"
    keys.mkdir()
    (keys / "key-fixture.xml").write_bytes(b"<fixture-key />")
    with closing(sqlite3.connect(fresh / "organizer.sqlite")) as database:
        assert database.execute("PRAGMA journal_mode = WAL").fetchone() == ("wal",)
        database.execute("PRAGMA wal_autocheckpoint = 0")
        database.execute("DROP TABLE library_scan_metadata")
        database.execute("DROP TABLE library_scan_checkpoint")
        database.execute("DROP INDEX library_file_series_lookup")
        database.execute("DROP VIEW library_file_numbered")
        database.execute("DROP VIEW active_inferred_volume_chapters")
        database.execute("ALTER TABLE library_content DROP COLUMN manual_volume")
        database.execute("DROP TABLE import_file_split")
        database.execute("DROP TABLE maintenance_reset")
        database.execute("DROP TABLE duplicate_review_decision")
        database.execute("DROP TABLE release_tag")
        database.execute("DROP TABLE library_intake_target")
        database.execute("ALTER TABLE file_operation DROP COLUMN intake_transfer")
        database.execute("DROP TABLE metadata_suggestion")
        database.execute("ALTER TABLE file_operation DROP COLUMN quarantine_keep_root")
        database.execute("ALTER TABLE file_operation DROP COLUMN quarantine_keep_base")
        database.execute("ALTER TABLE file_operation DROP COLUMN quarantine_keep_file")
        database.execute("ALTER TABLE file_operation DROP COLUMN quarantine_keep_path")
        database.execute("DROP TABLE edition_volume_release")
        database.execute("DROP TABLE edition_volume_manifest")
        database.execute("DROP TABLE preferred_chapter_release")
        database.execute("DROP TABLE chapter_completion")
        database.execute("DROP TABLE inferred_volume_chapters")
        database.execute("DROP TABLE series_collection_item")
        database.execute("DROP TABLE series_collection")
        database.execute("DROP TABLE series_cover_preference")
        database.execute("DROP TABLE library_sidecar_stamp")
        database.execute("ALTER TABLE library_file DROP COLUMN scan_stamp")
        database.execute("ALTER TABLE library_file DROP COLUMN scan_chapters")
        database.execute("ALTER TABLE library_file DROP COLUMN scan_chapter_source")
        database.execute("ALTER TABLE library_root DROP COLUMN last_scan")
        database.execute("DROP TABLE series_release_metadata")
        database.execute("DROP TABLE series_identity_alias")
        database.execute("DROP INDEX series_metadata_anilist_id")
        database.execute("DROP INDEX content_metadata_series")
        database.execute("ALTER TABLE series_metadata DROP COLUMN manual_tags")
        database.execute("ALTER TABLE series_metadata DROP COLUMN provider_details")
        database.execute("DELETE FROM schema_migration WHERE version >= 9")
        database.execute("""
            INSERT INTO series_metadata(id, series_key, title, kind, provider_id, updated_at)
            VALUES ('saved-series', 'series-key', 'Saved title', 'manga', 123, '2026-01-01')
            """)
        database.commit()
        assert (fresh / "organizer.sqlite-wal").stat().st_size > 32
        run_migrations(app, fresh)
    snapshot = verified_snapshot(fresh, 8, len(installed))
    assert (snapshot / "keys" / "key-fixture.xml").read_bytes() == b"<fixture-key />"
    assert entries(snapshot) == installed[:8]
    with closing(sqlite3.connect(snapshot / "organizer.sqlite")) as database:
        assert database.execute("SELECT title, provider_id FROM series_metadata").fetchone() == ("Saved title", 123)
    assert entries(fresh)[:8] == installed[:8]
    with closing(sqlite3.connect(fresh / "organizer.sqlite")) as database:
        assert database.execute("SELECT title, provider_id, provider_details FROM series_metadata").fetchone() == ("Saved title", 123, None)

    legacy = fixture / "legacy"
    legacy.mkdir()
    root_id, file_id, owner_id, server_id = (str(uuid4()) for _ in range(4))
    library = str(fixture / "Northbound")
    with closing(sqlite3.connect(legacy / "organizer.sqlite")) as database:
        database.executescript((MIGRATIONS / "0001-foundation.sql").read_text(encoding="utf-8"))
        database.executescript("""
            CREATE TABLE library_folder (
                root_id TEXT NOT NULL PRIMARY KEY REFERENCES library_root(id),
                path TEXT NOT NULL, name TEXT,
                kind TEXT NOT NULL CHECK (kind IN ('library', 'import'))
            );
            """)
        database.execute("INSERT INTO server_identity VALUES (1, ?, ?, 'saved password hash')", (server_id, owner_id))
        database.execute("INSERT INTO library_root(id, path_key, file_count) VALUES (?, ?, 1)", (root_id, library))
        database.execute("INSERT INTO library_folder VALUES (?, ?, 'Northbound', 'import')", (root_id, library))
        database.execute("INSERT INTO library_file VALUES (?, ?, '01.cbz', 'cbz', 5, '2026-01-01 00:00:00+00:00')",
                         (file_id, root_id))
        database.commit()

    blocked = fixture / "blocked"
    blocked.mkdir()
    copyfile(legacy / "organizer.sqlite", blocked / "organizer.sqlite")
    original = (blocked / "organizer.sqlite").read_bytes()
    (blocked / "upgrade-backups").write_text("Occupied backup path", encoding="utf-8")
    refusal = run_migrations(app, blocked, expected=1)
    assert "Database migration failed" in refusal.stderr
    assert (blocked / "organizer.sqlite").read_bytes() == original

    run_migrations(app, legacy)
    snapshot = verified_snapshot(legacy, 0, len(installed))
    with closing(sqlite3.connect(snapshot / "organizer.sqlite")) as database:
        assert database.execute("SELECT name FROM sqlite_schema WHERE name = 'schema_migration'").fetchone() is None
        assert database.execute("SELECT instance_id, owner_id FROM server_identity").fetchone() == (server_id, owner_id)
    if previous_app:
        restored = fixture / "restored"
        restored.mkdir()
        copyfile(snapshot / "organizer.sqlite", restored / "organizer.sqlite")
        with start_app(previous_app, restored, None, None) as url:
            wait_for_server(url)
            with closing(sqlite3.connect(restored / "organizer.sqlite")) as database:
                assert database.execute("SELECT instance_id, owner_id FROM server_identity").fetchone() == (server_id, owner_id)
                assert database.execute("SELECT id, relative_path FROM library_file").fetchone() == (file_id, "01.cbz")
    assert [row[:2] for row in entries(legacy)] == [row[:2] for row in installed]
    with closing(sqlite3.connect(legacy / "organizer.sqlite")) as database:
        assert database.execute("SELECT instance_id, owner_id, password_hash FROM server_identity").fetchone() == (
            server_id, owner_id, "saved password hash")
        assert database.execute("SELECT root_id, path, kind FROM library_folder").fetchone() == (root_id, library, "import")
        assert database.execute("SELECT id, content_id, series_title FROM library_file").fetchone() == (file_id, None, "Northbound")
        database.execute("UPDATE library_folder SET kind = 'both'")
        database.execute("INSERT INTO schema_migration VALUES (9999, 'future-schema', '2026-01-01')")
        database.commit()

    before = entries(legacy)
    refused = run_migrations(app, legacy, expected=1)
    assert "incompatible" in refused.stderr and entries(legacy) == before

    with start_app(app, fixture / "running", None, "migration fixture password") as url:
        wait_for_server(url)
        competing = run_migrations(app, fixture / "running", expected=1)
        assert "Could not lock the server data directory" in competing.stderr
    run_migrations(app, fixture / "running")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--previous-app", type=Path, help="Rehearse an offline restore with a previous server build")
    options = parser.parse_args()
    executable = options.app.resolve()
    with TemporaryDirectory() as fixture:
        verify(executable, Path(fixture), options.previous_app.resolve() if options.previous_app else None)
    print("Passed fresh schema, canonical series consolidation, verified WAL and legacy backups, fail-closed backup errors, idempotence, and future-version protection")
