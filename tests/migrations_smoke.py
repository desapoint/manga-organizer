"""Verify the packaged offline migration command against fresh and legacy SQLite files."""

import argparse
from contextlib import closing
import os
from pathlib import Path
import sqlite3
import subprocess
from tempfile import TemporaryDirectory
from uuid import uuid4


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


def verify(app, fixture):
    fresh = fixture / "fresh"
    run_migrations(app, fresh)
    installed = entries(fresh)
    assert [row[1] for row in installed] == [script.stem for script in sorted(MIGRATIONS.glob("*.sql"))]
    run_migrations(app, fresh)
    assert entries(fresh) == installed

    # Exercise the exact schema from the previous managed-file preview with an AniList assignment.
    with closing(sqlite3.connect(fresh / "organizer.sqlite")) as database:
        database.execute("ALTER TABLE file_operation DROP COLUMN quarantine_keep_file")
        database.execute("ALTER TABLE file_operation DROP COLUMN quarantine_keep_path")
        database.execute("DROP TABLE edition_volume_release")
        database.execute("DROP TABLE edition_volume_manifest")
        database.execute("DROP TABLE chapter_completion")
        database.execute("DROP TABLE inferred_volume_chapters")
        database.execute("DROP TABLE series_collection_item")
        database.execute("DROP TABLE series_collection")
        database.execute("ALTER TABLE series_metadata DROP COLUMN provider_details")
        database.execute("DELETE FROM schema_migration WHERE version >= 9")
        database.execute("""
            INSERT INTO series_metadata(id, series_key, title, kind, provider_id, updated_at)
            VALUES ('saved-series', 'series-key', 'Saved title', 'manga', 123, '2026-01-01')
            """)
        database.commit()
    run_migrations(app, fresh)
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

    run_migrations(app, legacy)
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


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", type=Path, required=True)
    executable = parser.parse_args().app.resolve()
    with TemporaryDirectory() as fixture:
        verify(executable, Path(fixture))
    print("Passed fresh schema, legacy migration, idempotence, and future-version protection")
