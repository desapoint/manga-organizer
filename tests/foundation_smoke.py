"""Exercise the published server through its browser-facing HTTP API."""

import argparse
from base64 import b64decode
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing, contextmanager
from hashlib import sha256
from http.cookiejar import CookieJar
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json
import os
import re
from shutil import copytree
import socket
import sqlite3
import struct
import subprocess
import tempfile
import threading
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import HTTPCookieProcessor, Request, build_opener
from uuid import UUID, uuid4
from zipfile import ZIP_DEFLATED, ZipFile
import zlib


BOOTSTRAP_VARIABLE = "MANGA_ORGANIZER_BOOTSTRAP_PASSWORD"
LIBRARY_ROOTS = "/api/library/roots"


class Browser:
    def __init__(self, url, cookies=None):
        self.url = url
        self.cookies = CookieJar() if cookies is None else cookies
        self.opener = build_opener(HTTPCookieProcessor(self.cookies))
        self.headers = None

    def request(self, path, expected, *, method="GET", body=None, token=None, setup_token=None):
        headers = {"X-Setup-Token": setup_token} if setup_token is not None else {}
        if token is not None:
            headers["X-CSRF-Token"] = token
        if isinstance(body, bytes):
            headers["Content-Type"] = "application/octet-stream"
        elif body is not None:
            headers["Content-Type"] = "application/json"
            body = json.dumps(body).encode()
        request = Request(self.url + path, data=body, headers=headers, method=method)
        try:
            # Allow scan polling and provider pacing on hosted Windows runners.
            response = self.opener.open(request, timeout=15)
        except HTTPError as error:
            response = error
        with response:
            assert response.status == expected, (method, path, response.status, expected)
            self.headers = response.headers
            return response.read()

    def csrf(self):
        return json.loads(self.request("/api/csrf", 200))["token"]

    def sign_in(self, password, expected=204):
        self.request("/api/session", expected, method="POST", body={"password": password}, token=self.csrf())

    def server_id(self):
        return UUID(json.loads(self.request("/api/server", 200))["id"])

    def roots(self):
        return json.loads(self.request(LIBRARY_ROOTS, 200))

    def files(self, root_id, query=""):
        return json.loads(self.request(f"{LIBRARY_ROOTS}/{root_id}/files{query}", 200))

    def series(self, root_id, query=""):
        return json.loads(self.request(f"{LIBRARY_ROOTS}/{root_id}/series{query}", 200))

    def scan(self, root_id, expected_state="complete", *, full=False):
        path = f"{LIBRARY_ROOTS}/{root_id}/scan" + ("?full=true" if full else "")
        self.request(path, 202, method="POST", token=self.csrf())
        for _ in range(80):
            root = next(root for root in self.roots() if root["id"] == root_id)
            if root["state"] not in {"queued", "scanning"}:
                assert root["state"] == expected_state, root
                return root
            time.sleep(0.1)
        raise AssertionError("Scan did not complete")


@contextmanager
def start_app(executable, data_dir, media, password, *, expect_setup=False, metadata_service=None):
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        url = f"http://127.0.0.1:{listener.getsockname()[1]}"
    env = {key: value for key, value in os.environ.items() if not key.upper().startswith("MANGA_ORGANIZER_LIBRARY_ROOTS__")}
    env.update(ASPNETCORE_URLS=url, MANGA_ORGANIZER_DATA_DIR=str(data_dir))
    if media is not None:
        env["MANGA_ORGANIZER_LIBRARY_ROOTS__0"] = str(media / "primary")
        env["MANGA_ORGANIZER_LIBRARY_ROOTS__1"] = str(media / "archive")
    env.pop(BOOTSTRAP_VARIABLE, None)
    if password is not None:
        env[BOOTSTRAP_VARIABLE] = password
    env["MANGA_ORGANIZER_METADATA_SERVICE_URL"] = metadata_service or "http://127.0.0.1:1/graphql"
    log_path = data_dir.parent / f"server-{uuid4().hex}.log"
    with log_path.open("wb") as log, subprocess.Popen([executable], env=env, stdout=log, stderr=log) as process:
        try:
            if expect_setup:
                pattern = re.compile(rf"{re.escape(url)}/#setup=([A-F0-9]{{64}})")
                for _ in range(60):
                    if match := pattern.search(log_path.read_text(errors="replace")):
                        break
                    time.sleep(0.25)
                else:
                    raise RuntimeError("The server did not print a first-launch link")
                yield url, match[1]
            else:
                yield url
        except Exception:
            print(re.sub(r"#setup=[A-F0-9]{64}", "#setup=[redacted]", log_path.read_text(errors="replace")))
            raise
        finally:
            process.terminate()
            process.wait(timeout=15)


@contextmanager
def start_container(image, volume, media, password, *, readonly=True):
    name = f"manga-smoke-{uuid4().hex}"
    command = ["docker", "run", "--detach", "--name", name, "-p", "127.0.0.1::8787",
               "--mount", f"type=volume,source={volume},target=/config",
               "--mount", f"type=bind,source={media},target=/media" + (",readonly" if readonly else ""),
               "--env", "MANGA_ORGANIZER_METADATA_SERVICE_URL=http://127.0.0.1:1/graphql",
               "--env", "MANGA_ORGANIZER_LIBRARY_ROOTS__0=/media/primary",
               "--env", "MANGA_ORGANIZER_LIBRARY_ROOTS__1=/media/archive"]
    env = os.environ.copy()
    env.pop(BOOTSTRAP_VARIABLE, None)
    if password is not None:
        command.extend(["--env", BOOTSTRAP_VARIABLE])
        env[BOOTSTRAP_VARIABLE] = password
    subprocess.run([*command, image], env=env, check=True, stdout=subprocess.DEVNULL)
    try:
        address = subprocess.check_output(["docker", "port", name, "8787/tcp"], text=True).strip()
        yield f"http://{address}"
        container_uid = subprocess.check_output(["docker", "exec", name, "id", "-u"], text=True).strip()
        assert container_uid != "0", "The container must run without root."
    except Exception:
        subprocess.run(["docker", "logs", name], check=False)
        raise
    finally:
        subprocess.run(["docker", "rm", "-f", name], check=True, stdout=subprocess.DEVNULL)


def wait_for_server(url):
    opener = build_opener()
    for _ in range(60):
        try:
            with opener.open(f"{url}/healthz", timeout=5) as response:
                assert response.status == 200
                return
        except HTTPError as error:
            if error.code != 404:
                raise
        except (URLError, ConnectionError):
            pass
        time.sleep(0.25)
    raise RuntimeError("Server did not start")


def verify_web_setup(executable, fixture, media):
    appdata = fixture / "first-run-data"
    env = dict(os.environ, ASPNETCORE_URLS="http://0.0.0.0:0", MANGA_ORGANIZER_DATA_DIR=str(appdata))
    env.pop(BOOTSTRAP_VARIABLE, None)
    unattended = subprocess.run([executable], env=env, capture_output=True, text=True, timeout=15)
    assert unattended.returncode == 1 and BOOTSTRAP_VARIABLE in unattended.stderr
    assert "Unhandled exception" not in unattended.stderr

    password = uuid4().hex
    setup = {"password": password, "confirmPassword": password, "libraryPath": str(media / "primary")}
    with start_app(executable, appdata, None, None, expect_setup=True) as (url, old_token):
        browser = Browser(url)
        assert json.loads(browser.request("/api/setup", 200)) == {"libraryConfigured": False}
        browser.request("/api/server", 401)
        browser.request(LIBRARY_ROOTS, 404)
        browser.request("/healthz", 404)
        browser.request("/api/setup", 403, method="POST", body=setup)
        browser.request("/api/setup/folders", 403)
        location = urlencode({"path": str(media)})
        folders = json.loads(browser.request(f"/api/setup/folders?{location}", 200, setup_token=old_token))
        assert "primary" in {folder["name"] for folder in folders["folders"]}
        assert folders["breadcrumbs"][-1]["path"] == folders["path"]

    with start_app(executable, appdata, None, None, expect_setup=True) as (url, token):
        browser = Browser(url)
        assert token != old_token
        browser.request("/api/setup", 403, method="POST", body=setup, setup_token=old_token)
        browser.request("/api/setup", 400, method="POST", body=dict(setup, confirmPassword="different password"), setup_token=token)
        browser.request("/api/setup", 400, method="POST", body=dict(setup, libraryPath=str(appdata)), setup_token=token)
        browser.request("/api/setup", 204, method="POST", body=setup, setup_token=token)
        wait_for_server(url)
        browser.request("/api/setup", 404, method="POST", body=setup, setup_token=token)
        browser.sign_in(password)
        identity = browser.server_id()
        (root,) = browser.roots()
        assert Path(root["path"]).samefile(media / "primary"), root["path"]
        browser.scan(root["id"])
        original = browser.files(root["id"], "?limit=100")
        original_series = browser.series(root["id"], "?limit=100")

    # Restore the schema used before content identities were added, keeping the previous inventory.
    with closing(sqlite3.connect(appdata / "organizer.sqlite")) as database:
        database.executescript("""
            DROP TABLE schema_migration;
            DROP TABLE library_sidecar_stamp;
            ALTER TABLE library_file DROP COLUMN scan_stamp;
            ALTER TABLE library_file DROP COLUMN scan_chapters;
            ALTER TABLE library_file DROP COLUMN scan_chapter_source;
            ALTER TABLE library_root DROP COLUMN last_scan;
            DROP TABLE series_cover_preference;
            DROP TABLE series_release_metadata;
            DROP TABLE series_identity_alias;
            DROP VIEW library_file_numbered;
            DROP VIEW active_inferred_volume_chapters;
            DROP TABLE import_file_split;
            DROP TABLE metadata_suggestion;
            DROP TABLE release_tag;
            DROP TABLE maintenance_reset;
            DROP TABLE duplicate_review_decision;
            DROP TABLE library_intake_target;
            DROP TABLE edition_volume_release;
            DROP TABLE edition_volume_manifest;
            DROP TABLE preferred_chapter_release;
            DROP TABLE chapter_completion;
            DROP TABLE inferred_volume_chapters;
            DROP TABLE series_collection_item;
            DROP TABLE series_collection;
            DROP TABLE file_operation;
            DROP TABLE reading_progress_event;
            DROP TABLE reading_progress;
            DROP TABLE content_chapter;
            DROP TABLE content_metadata;
            DROP TABLE series_metadata_binding;
            DROP TABLE series_metadata;
            DROP INDEX library_file_content;
            DROP INDEX library_file_series;
            DROP INDEX library_file_series_lookup;
            ALTER TABLE library_file DROP COLUMN content_id;
            ALTER TABLE library_file DROP COLUMN series_key;
            ALTER TABLE library_file DROP COLUMN series_title;
            ALTER TABLE library_file DROP COLUMN volume_hint;
            ALTER TABLE library_file DROP COLUMN chapter_hint;
            DROP TABLE library_content;
            ALTER TABLE library_folder RENAME TO current_library_folder;
            CREATE TABLE library_folder (
                root_id TEXT NOT NULL PRIMARY KEY REFERENCES library_root(id),
                path TEXT NOT NULL,
                name TEXT,
                kind TEXT NOT NULL CHECK (kind IN ('library', 'import'))
            );
            INSERT INTO library_folder SELECT root_id, path, name, kind FROM current_library_folder ORDER BY rowid;
            DROP TABLE current_library_folder;
            UPDATE library_root SET state = 'scanning';
        """)

    with start_app(executable, appdata, None, None) as url:
        wait_for_server(url)
        browser = Browser(url)
        browser.request("/api/setup", 404)
        browser.sign_in(password)
        assert browser.server_id() == identity
        assert [configured["id"] for configured in browser.roots()] == [root["id"]]
        assert browser.roots()[0]["state"] == "interrupted"
        migrated = browser.files(root["id"], "?limit=100")
        assert migrated == {"total": original["total"], "items": [
            dict(row, contentId=None, sha256=None, indexedLocations=None) for row in original["items"]
        ]}
        assert browser.series(root["id"], "?limit=100") == original_series
        dual = {"path": root["path"], "name": root["name"], "kind": "both"}
        saved = json.loads(browser.request(f"{LIBRARY_ROOTS}/{root['id']}", 200, method="PUT", token=browser.csrf(), body=dual))
        assert saved["id"] == root["id"] and browser.roots()[0]["kind"] == "both"
        browser.scan(root["id"])
        backfilled = browser.files(root["id"], "?limit=100")
        assert [(row["id"], row["sha256"]) for row in backfilled["items"]] == [
            (row["id"], row["sha256"]) for row in original["items"]
        ]
        assert all(row["contentId"] for row in backfilled["items"])


def verify_folder_settings(owner, media):
    primary, archive = owner.roots()
    server_media = Path(primary["path"]).parent
    folders = json.loads(owner.request("/api/library/folders", 200))
    assert folders["folders"], "No server disks or mounts appeared in the picker"
    assert folders["breadcrumbs"] == []
    location = urlencode({"path": str(server_media)})
    browse = json.loads(owner.request(f"/api/library/folders?{location}", 200))
    assert {"extra", "intake", "primary", "archive"} <= {folder["name"] for folder in browse["folders"]}
    assert [folder["path"] for folder in browse["breadcrumbs"][-2:]] == [browse["parent"], browse["path"]]
    owner.request("/api/library/folders?path=relative", 400)
    if os.name != "nt":
        link = urlencode({"path": str(server_media / "primary" / "linked-folder")})
        owner.request(f"/api/library/folders?{link}", 400)

    extra = {"path": str(server_media / "extra"), "name": "Extras", "kind": "library"}
    owner.request(LIBRARY_ROOTS, 400, method="POST", body=extra)
    owner.request(LIBRARY_ROOTS, 400, method="POST", token=owner.csrf(), body=dict(extra, path=primary["path"]))
    saved = json.loads(owner.request(LIBRARY_ROOTS, 200, method="POST", token=owner.csrf(), body=extra))
    folder_url = f"{LIBRARY_ROOTS}/{saved['id']}"
    changed = json.loads(owner.request(folder_url, 200, method="PUT", token=owner.csrf(), body=dict(extra, name="Reference Manga")))
    assert changed["id"] == saved["id"] and changed["name"] == "Reference Manga"
    moved_binding = json.loads(owner.request(folder_url, 200, method="PUT", token=owner.csrf(),
                                             body=dict(extra, path=str(server_media / "spare"))))
    assert moved_binding["id"] != saved["id"]
    owner.request(f"{folder_url}/files", 404)
    owner.request(f"{LIBRARY_ROOTS}/{moved_binding['id']}", 204, method="DELETE", token=owner.csrf())

    intake = {"path": str(server_media / "intake"), "name": "Incoming Manga", "kind": "import"}
    added_intake = json.loads(owner.request(LIBRARY_ROOTS, 200, method="POST", token=owner.csrf(), body=intake))
    owner.scan(added_intake["id"])
    (incoming,) = owner.files(added_intake["id"])["items"]
    assert incoming["relativePath"] == "Unmatched.cbz"
    shared = json.loads(owner.request(f"{LIBRARY_ROOTS}/{added_intake['id']}", 200, method="PUT", token=owner.csrf(),
                                      body=dict(intake, kind="both")))
    assert shared["id"] == added_intake["id"] and shared["kind"] == "both"
    assert [root["id"] for root in owner.roots()].count(shared["id"]) == 1
    assert owner.files(shared["id"])["items"] == [incoming]
    verify_series_groups(owner, shared["id"], media / "intake")
    verify_cover_art(owner, shared["id"], media / "intake")
    verify_lowest_owned_cover(owner, shared["id"], media / "intake")
    verify_live_scan(owner, shared["id"], media / "intake")
    verify_series_metadata(owner, shared["id"], media / "intake")
    owner.request(f"{LIBRARY_ROOTS}/{archive['id']}", 204, method="DELETE", token=owner.csrf())
    assert (media / "archive" / "Volume.epub").exists()
    assert (media / "intake" / "Unmatched.cbz").exists()
    assert (media / "extra" / "Keep.cbz").exists()
    return owner.roots()


def verify_series_groups(owner, root_id, path):
    series_title = "A Story About a Knight"
    fixtures = [f"{series_title}/{series_title} v{volume:02}.cbz" for volume in range(1, 53)]
    fixtures += [f"{series_title} v53 [Group A] [Ongoing].cbz", "Northbound [Ongoing]/Vol.02/001.cbz",
                 "Northbound [Ongoing]/Ch. 1.5.cbz", "Northbound [Ongoing]/Northbound 03.cbz",
                 "[Group B] Northbound Chapter 04.cbz", "Northbound/Tome 03/Ch. 15.5 — A chapter title/001.cbz",
                 "Northbound/[Group B] Ch. 16 - 1984 (2022) (Digital).cbz",
                 "Incoming/001.cbz", "Incoming/002.cbz", "Incoming/Ch.004 - Unknown story.cbz",
                 "Chapters/Ch.004 - Unknown story.cbz", "Undead Unluck/Another Series v01.cbz",
                 "Different Title 2.cbz", "Different Title 3.cbz", "Different Title 2 (2022).cbz"]
    numbered_releases = {
        "A Kiss with a Cat": [f"A Kiss with a Cat v{n:02} ({2022 if n < 5 else 2023}) (Digital) (1r0n).cbz"
                             for n in range(1, 6)],
        "Akumetsu": [f"Akumetsu - Tome {n:02} [CBR] [Scan DP] [FR] [1500x1200].cbr" for n in range(1, 8)],
        "Undead Unluck": [f"Undead Unluck {n} (2022) (Digital) (1r0n).cbz" for n in range(107, 119)],
    }
    chapter_titles = [
        (1, "Undead and Unluck"), (4, "Comment utiliser mon unluck"), (5, "Tu es qui, toi"),
        (6, "Tu n'as pas du tout changé"), (7, "Le changement, c'est maintenant"),
        (8, "Aimes-tu ma façon de changer"), (9, "Unis par nos ennemis"), (10, "C'est le costume que je souhaite"),
    ]
    numbered_releases["Undead Unluck"].extend(
        f"Undead Unluck/Vol.{1 if chapter < 8 else 2:02} Ch.{chapter:04} - {title} (fr) [Unexpected Team].cbz"
        for chapter, title in chapter_titles
    )
    numbered_releases["Akumetsu"].extend([
        "Akumetsu - Integral 18 Tomes/Akumetsu - Tome 18 [CBR] [Scan DP] [FR] [1500x1200] Fin.cbr",
        "Akumetsu - Integral 18 Tomes/Akumetsu Chapitre 19 Provider AA22D44D 1500x1200.cbz",
        "Akumetsu Page 001 ArchiveSource high resolution.cbz",
    ])
    for releases in numbered_releases.values():
        fixtures.extend(releases)
    doujin_releases = {
        "A Rainy Festival 4": "(2021-03 Akihabara Chou Doujinsai) [Studio (Artist)] A Rainy Festival 4 (Fixture Franchise) [English].cbz",
        "Town on a Hill": "(Air Comiket 3) [Studio(Artist)] Town on a Hill.cbz",
        "Side Story 22.0": "(C101) [Studio (Artist)] Side Story 22.0 (Original) [English] [Digital].cbz",
        "Archive Route 2": "(C103) [Studio (Artist)] Archive Route 2 — The Archive Arc (Fixture Franchise) [English] =Imari=.cbz",
        "Pet's Great Escape": "(C104) [Studio (Artist)]Pet's Great Escape [English] {Translator}.cbz",
        "Evening Study": "(COMITIA101) [Studio (Artist)] Evening Study & [English] [TL].cbz",
        "Blue Notebook": "(COMITIA118) [Studio (Artist)] Blue Notebook [English].cbz",
        "Spring Sketches": "(COMITIA124) [Studio (Artist)] Spring Sketches [English] =Translators=.cbz",
        "Garden Route": "(COMITIA130) [Studio (Artist)] Garden Route 1-3 - The Garden Path 1-3 [English].cbz",
        "Kitto Haru Comes in Spring": "(COMITIA137) [Studio (Artist)] Kitto Haru Comes in Spring (English) (Translator).cbz",
        "Tea Time - At Dawn": "(COMIKET104) [Studio] Tea Time - At Dawn [English].cbz",
        "Koi no Silver Himitsu": "(COMITIA137) [Studio (Artist)] Koi no Silver Himitsu The Secret of Silver (English).cbz",
        "Haru no Tabiji The Animation": "(COMITIA142) [Studio] Haru no Tabiji The Animation (English).cbz",
    }
    fixtures.extend(doujin_releases.values())
    for relative in fixtures:
        file = path / relative
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_bytes(relative.encode("utf-8"))

    assert owner.scan(root_id)["fileCount"] == len(fixtures) + 1
    groups = owner.series(root_id)
    by_title = {group["title"]: group for group in groups["items"]}
    for title, releases in numbered_releases.items():
        group = by_title[title]
        assert group["fileCount"] == len(releases)
        files = owner.files(root_id, f"?{urlencode({'seriesKey': group['key']})}")
        assert {row["relativePath"] for row in files["items"]} == set(releases)
    assert groups["total"] == len(by_title) == 14 + len(doujin_releases)
    for title, filename in doujin_releases.items():
        path_query = urlencode({"seriesKey": by_title[title]["key"], "view": "all"})
        (entry,) = owner.files(root_id, "?" + path_query)["items"]
        assert entry["relativePath"] == filename and entry["filenameEvidence"]["title"] == title
        assert entry["filenameEvidence"]["event"] and entry["filenameEvidence"]["creator"]
        assert not entry["associationConfirmed"]
    arc = owner.files(root_id, "?seriesKey=" + by_title["Archive Route 2"]["key"])["items"][0]["filenameEvidence"]
    assert arc["alternateTitle"] == "The Archive Arc" and "Fixture Franchise" in arc["notes"]
    garden = owner.files(root_id, "?seriesKey=" + by_title["Garden Route"]["key"])["items"][0]["filenameEvidence"]
    assert garden["alternateTitle"] == "The Garden Path" and "1-3" in garden["notes"]
    assert garden["event"] == "COMITIA130" and garden["creator"] == "Studio (Artist)"
    bilingual = owner.files(root_id, "?seriesKey=" + by_title["Koi no Silver Himitsu"]["key"])["items"][0]["filenameEvidence"]
    assert bilingual["alternateTitle"] == "The Secret of Silver"
    assert owner.series(root_id, "?view=all&search=Incoming%2F001.cbz")["items"][0]["lookupTitle"] == ""
    assert by_title[series_title]["fileCount"] == 53
    assert by_title["Northbound"]["fileCount"] == 6
    assert by_title["Incoming/001.cbz"]["fileCount"] == by_title["Incoming/002.cbz"]["fileCount"] == 1
    assert by_title["Incoming/Ch.004 - Unknown story.cbz"]["fileCount"] == 1
    assert by_title["Chapters/Ch.004 - Unknown story.cbz"]["fileCount"] == 1
    assert by_title["Another Series"]["fileCount"] == 1
    assert by_title["Different Title 2"]["key"] != by_title["Different Title 3"]["key"]
    assert by_title["Different Title 2 (2022)"]["fileCount"] == 1
    assert owner.series(root_id, "?limit=1") == {"total": 14 + len(doujin_releases), "items": groups["items"][:1]}
    query = urlencode({"seriesKey": by_title[series_title]["key"], "limit": 50})
    first = owner.files(root_id, f"?{query}")
    second = owner.files(root_id, f"?{query}&offset=50")
    assert first["total"] == second["total"] == 53
    assert {row["relativePath"] for row in first["items"] + second["items"]} == set(fixtures[:53])
    owner.request(f"{LIBRARY_ROOTS}/{root_id}/series?offset=-1", 400)
    owner.request(f"{LIBRARY_ROOTS}/{root_id}/files?seriesKey=invalid", 400)


def synthetic_png(red, green, blue, width=12, height=18):
    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body))

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    pixels = (b"\0" + bytes((red, green, blue)) * width) * height
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b"")


def fixed_size_png(image, size):
    # Pad within a valid ancillary chunk and keep the PNG end marker last.
    text = b"Note\0" + b"x" * (size - len(image) - 17)
    tag = b"tEXt"
    return image[:-12] + struct.pack(">I", len(text)) + tag + text + struct.pack(">I", zlib.crc32(tag + text)) + image[-12:]

def webp_dimensions(image):
    assert image[:4] == b"RIFF" and image[8:12] == b"WEBP"
    if image[12:16] == b"VP8X":
        return 1 + int.from_bytes(image[24:27], "little"), 1 + int.from_bytes(image[27:30], "little")
    if image[12:16] == b"VP8L":
        bits = int.from_bytes(image[21:25], "little")
        return 1 + (bits & 0x3fff), 1 + ((bits >> 14) & 0x3fff)
    assert image[12:16] == b"VP8 " and image[23:26] == b"\x9d\x01\x2a"
    width, height = struct.unpack_from("<HH", image, 26)
    return width & 0x3fff, height & 0x3fff


def stored_rar(entries, *, rar5=False):
    """Build a single-volume RAR fixture from synthetic entries without an external encoder."""
    if not rar5:
        def block(header):
            return struct.pack("<H", zlib.crc32(header) & 0xffff) + header

        data = b"Rar!\x1a\x07\0" + block(struct.pack("<BHH", 0x73, 0, 13) + bytes(6))
        for name, body in entries:
            name = name.encode("ascii")
            header = struct.pack("<BHHIIBIIBBHI", 0x74, 0x8000, 32 + len(name), len(body), len(body), 3,
                                 zlib.crc32(body), 0x00210000, 20, 0x30, len(name), 0o100644)
            data += block(header + name) + body
        return data + block(struct.pack("<BHH", 0x7b, 0, 7))

    def vint(value):
        result = bytearray()
        while value >= 128:
            result.append((value & 127) | 128)
            value >>= 7
        result.append(value)
        return bytes(result)

    def block(header):
        sized = vint(len(header)) + header
        return struct.pack("<I", zlib.crc32(sized)) + sized

    data = b"Rar!\x1a\x07\x01\0" + block(bytes((1, 0, 0)))
    for name, body in entries:
        name = name.encode("ascii")
        header = bytes((2, 2)) + vint(len(body)) + bytes((4,)) + vint(len(body)) + bytes((0,))
        header += struct.pack("<I", zlib.crc32(body)) + bytes((0, 0)) + vint(len(name)) + name
        data += block(header) + body
    return data + block(bytes((5, 0, 0)))


def verify_lowest_owned_cover(owner, root_id, path):
    early, late, page = synthetic_png(14, 90, 180, 19, 33), synthetic_png(80, 120, 20, 23, 38), synthetic_png(10, 12, 65)

    def book(relative, image):
        file = path / relative
        file.parent.mkdir(parents=True, exist_ok=True)
        with ZipFile(file, "w") as archive:
            archive.writestr("1.png", image)

    book("Chapter order/Chapter 10.cbz", late)
    book("Chapter order/Chapter 2.5.cbz", early)
    (path / "Chapter order" / "Chapter 1.cbr").write_bytes(b"An unusable archive")
    book("Volume order/Tome 10/Chapter 1.cbz", late)
    book("Volume order/Tome 2/Chapter 10.cbz", late)
    book("Volume order/Tome 2/Chapter 3.cbz", early)
    book("Bare order/Bare order 10 (2022) (Digital).cbz", late)
    book("Bare order/Bare order 2 (2022) (Digital).cbz", early)
    for rar5 in (False, True):
        folder = path / ("Archive coverage Five" if rar5 else "Archive coverage Four")
        folder.mkdir()
        cover_meta = b'<ComicInfo><Pages><Page Image="1" Type="FrontCover"/></Pages></ComicInfo>'
        (folder / "Chapter 2.cbr").write_bytes(stored_rar([
            ("pages/1.png", page), ("pages/2.png", early), ("pages/front-cover.png", late), ("ComicInfo.xml", cover_meta),
        ], rar5=rar5))

    owner.scan(root_id)
    for name in ("Chapter order", "Volume order", "Bare order", "Archive coverage Five", "Archive coverage Four"):
        (series,) = owner.series(root_id, "?" + urlencode({"search": name}))["items"]
        assert series["title"] == name and series["hasCover"]
        listing = owner.series(root_id, "?" + urlencode({"seriesKey": series["key"]}))
        assert listing == {"total": 1, "items": [series]}
        series_url = f"{LIBRARY_ROOTS}/{root_id}/series/{series['key']}/cover"
        assert webp_dimensions(owner.request(series_url, 200)) == (19, 33)
        files = owner.files(root_id, "?" + urlencode({"seriesKey": series["key"]}))["items"]
        assert all(item["seriesCoverKey"] == series["key"] for item in files)
        if name.startswith("Archive coverage"):
            (file,) = files
            assert file["extension"] == "cbr" and file["hasCover"]
            assert webp_dimensions(owner.request(f"{LIBRARY_ROOTS}/{root_id}/files/{file['id']}/cover", 200)) == (19, 33)
        if name == "Chapter order":
            (missing,) = [file for file in files if file["extension"] == "cbr"]
            assert not missing["hasCover"] and missing["seriesCoverKey"] == series["key"]


def verify_cover_art(owner, root_id, path):
    first_page = synthetic_png(32, 160, 122, 12, 18)
    later_page = synthetic_png(221, 42, 70, 26, 39)
    detailed_cover = synthetic_png(42, 70, 225, 1600, 2400)
    # Independently encoded 21×33 JPEG and 19×30 WebP examples.
    jpeg = b64decode("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAUDBAQEAwUEBAQFBQUGBwwIBwcHBw8LCwkMEQ8SEhEPERETFhwXExQaFR"
                     "ERGCEYGh0dHx8fExciJCIeJBweHx7/2wBDAQUFBQcGBw4ICA4eFBEUHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4e"
                     "Hh4eHh4eHh4eHh4eHh4eHh4eHh4eHh7/wAARCAAhABUDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAf/xA"
                     "AUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFgEBAQEAAAAAAAAAAAAAAAAAAAUI/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/a"
                     "AAwDAQACEQMRAD8Ai4DRq6AAAAAAAAAA/9k=")
    webp = b64decode("UklGRkgAAABXRUJQVlA4IDwAAABQAwCdASoTAB4APlEkj0WjoiEUBAA4BQSygDsAfgAAZN/sAAD9xx//"
                     "8guWF1yNf//ILlhdcUo2YiQAAAA=")
    src = path / "Éclipse"
    src.mkdir()
    first_archive = src / "Éclipse v01.cbz"
    with ZipFile(first_archive, "w", compression=ZIP_DEFLATED) as archive:
        archive.writestr("./unsafe.png", later_page)
        archive.writestr("pages/1.png", b"Corrupt first entry")
        archive.writestr("pages/10.png", later_page)
        archive.writestr("pages/2.png", first_page)
    with ZipFile(src / "Éclipse v02.cbz", "w", compression=ZIP_DEFLATED) as archive:
        archive.writestr("pages/1.png", first_page)
        archive.writestr("pages/2.png", detailed_cover)
        archive.writestr("pages/front-cover.png", later_page)
        archive.writestr("ComicInfo.xml", '<ComicInfo><Pages><Page Image="1" Type="FrontCover"/></Pages></ComicInfo>')
    with ZipFile(src / "Éclipse v03.zip", "w", compression=ZIP_DEFLATED) as archive:
        archive.writestr("pages/1.png", first_page)
        archive.writestr("pages/front-cover.png", later_page)
        archive.writestr("ComicInfo.xml", '<ComicInfo><Pages><Page Image="999" Type="FrontCover"/></Pages></ComicInfo>')
    (src / "Éclipse v04.cbz").write_bytes(first_archive.read_bytes())
    (src / "cover.jpg").write_bytes(jpeg)
    (src / "cover-1.webp").write_bytes(webp)
    (src / "cover-2.png").write_bytes(fixed_size_png(later_page, 256))
    owner.scan(root_id)
    assert any(root["id"] == root_id and root["hasCovers"] for root in owner.roots())
    (series,) = owner.series(root_id, "?search=eclipse&coverOnly=true")["items"]
    assert series["title"] == "Éclipse" and series["hasCover"]
    url = f"{LIBRARY_ROOTS}/{root_id}/series/{series['key']}/cover"
    sidecar = owner.request(url, 200)
    assert webp_dimensions(sidecar) == (21, 33)
    assert owner.headers.get_content_type() == "image/webp" and owner.headers["X-Content-Type-Options"] == "nosniff"
    Browser(owner.url).request(url, 401)

    files = owner.files(root_id, f"?seriesKey={series['key']}")["items"]
    assert len(files) == 4 and all(file["hasCover"] for file in files)
    archive_urls = [f"{LIBRARY_ROOTS}/{root_id}/files/{file['id']}/cover" for file in files]
    archive_covers = [owner.request(cover_url, 200) for cover_url in archive_urls]
    assert [webp_dimensions(cover) for cover in archive_covers] == [(12, 18), (1365, 2048), (26, 39), (12, 18)]
    assert archive_covers[0] == archive_covers[3]
    Browser(owner.url).request(archive_urls[0], 401)

    offline_archive = first_archive.with_suffix(".offline")
    first_archive.rename(offline_archive)
    try:
        assert owner.request(archive_urls[0], 200) == archive_covers[0]
        (src / "cover.jpg").write_bytes(b"Invalid cover")
        assert owner.request(url, 200) == sidecar
    finally:
        offline_archive.rename(first_archive)
    owner.scan(root_id)
    assert webp_dimensions(owner.request(url, 200)) == (19, 30)
    (src / "cover-1.webp").unlink()
    local_png = src / "cover-2.png"
    previous = local_png.stat()
    replacement = synthetic_png(45, 70, 130, 12, 18)
    local_png.write_bytes(fixed_size_png(replacement, previous.st_size))
    os.utime(local_png, ns=(previous.st_atime_ns, previous.st_mtime_ns))
    owner.scan(root_id)
    assert webp_dimensions(owner.request(url, 200)) == (12, 18)
    local_png.unlink()
    owner.scan(root_id)
    assert owner.request(url, 200) == archive_covers[0]
    all_series = owner.series(root_id, "?sort=files")["items"]
    assert all_series[0]["fileCount"] == 53 and any(not grouped["hasCover"] for grouped in all_series)
    owner.request(f"{LIBRARY_ROOTS}/{root_id}/series?sort=unknown", 400)


def verify_live_scan(owner, root_id, path):
    probe = path / "Scan progress fixture.pdf"
    with probe.open("wb") as stream:
        stream.truncate(32 * 1024 * 1024)

    accepted = json.loads(owner.request(f"{LIBRARY_ROOTS}/{root_id}/scan", 202, method="POST", token=owner.csrf()))
    assert accepted["phase"] == "queued" and accepted["discoveredFiles"] == 0
    snapshots = [accepted]
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        root = next(root for root in owner.roots() if root["id"] == root_id)
        if root["state"] not in {"queued", "scanning"}:
            break
        if root["scanProgress"] is not None:
            snapshots.append(root["scanProgress"])
        time.sleep(0.01)
    assert root["state"] == "complete" and root["scanProgress"] is None
    for snapshot in snapshots:
        assert 0 <= snapshot["verifiedFiles"] <= snapshot["processedFiles"] <= snapshot["discoveredFiles"]
        assert snapshot["hashedFiles"] + snapshot["reusedFiles"] == snapshot["verifiedFiles"]
        assert 0 <= snapshot["bytesRead"] <= snapshot["totalBytes"]
        assert 0 <= snapshot["coversFound"] <= snapshot["seriesChecked"] <= snapshot["seriesTotal"]
    probe.unlink()
    owner.scan(root_id)


def verify_series_metadata(owner, root_id, path):
    (series,) = owner.series(root_id, "?search=A+Story+About+a+Knight")["items"]
    series_key = series["key"]
    url = f"{LIBRARY_ROOTS}/{root_id}/series/{series_key}"
    before = json.loads(owner.request(f"{url}/details", 200))
    assert before["availableChapters"] == 0 and before["volumes"][0] == "1"
    edit = {"title": "Knight Stories", "alternateTitle": "Chant des chevaliers", "kind": "doujin", "totalChapters": 10}
    owner.request(f"{url}/metadata", 400, method="PUT", body=edit)
    owner.request(f"{url}/metadata", 200, method="PUT", token=owner.csrf(), body=edit)
    assert owner.series(root_id, "?search=chant")["items"][0]["title"] == "Knight Stories"
    assert owner.files(root_id, "?search=chant")["total"] == 53
    assert owner.files(root_id, "?search=v01.cbz")["total"] >= 1

    files = owner.files(root_id, "?" + urlencode({"seriesKey": series_key, "limit": 100}))["items"]
    (volume,) = [file for file in files if file["volume"] == "1"]
    coverage = {"contentId": volume["contentId"], "volume": "01", "chapters": ["1", "1.50", "2", "2.0"]}
    owner.request(f"{url}/coverage", 204, method="PUT", token=owner.csrf(), body=coverage)
    details = json.loads(owner.request(f"{url}/details", 200))
    assert details["metadata"]["kind"] == "doujin" and details["metadata"]["totalChapters"] == 10
    assert details["availableChapters"] == 3 and details["chapters"][1] == {
        "volume": "1", "chapter": "1.5", "contentId": volume["contentId"], "readableFiles": 1, "completed": False,
    }
    assert details["reading"]["contentId"] == volume["contentId"] and details["reading"]["pageIndex"] is None

    original = path / volume["relativePath"]
    original.rename(path / "Same content without original title.cbz")
    owner.scan(root_id)
    after = json.loads(owner.request(f"{url}/details", 200))
    assert after["reading"]["relativePath"] != details["reading"]["relativePath"]
    assert dict(after, reading=details["reading"]) == details


def verify_external_metadata(executable, fixture):
    media = fixture / "metadata-media"
    (media / "primary").mkdir(parents=True)
    (media / "archive").mkdir()
    (media / "primary" / "Issue 01.pdf").write_bytes(b"Unique synthetic issue for matching tests")
    cover = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/fixture.jpg"
    media_result = {"id": 123, "title": {"english": "Synthetic provider title", "native": "本"},
                    "description": "Metadata &amp; tags<br><br><i>Second paragraph</i>", "status": "FINISHED", "chapters": 12, "volumes": 3,
                    "format": "ONE_SHOT", "countryOfOrigin": "JP", "startDate": {"year": 2011},
                    "endDate": {"year": 2012}, "genres": ["Drama", "drama"],
                    "coverImage": {"medium": cover}, "staff": {"edges": [
                        {"role": "Story & Art", "node": {"name": {"full": "Fixture Author"}}}]},
                    "tags": [{"name": "Doujinshi"}, {"name": "Hidden outcome", "isMediaSpoiler": True}]}
    no_cover = {"id": 456, "title": {"romaji": "Cover fallback"}, "status": "FINISHED", "chapters": 1,
                "coverImage": {"medium": "https://s4.anilist.co.invalid/file/anilistcdn/media/manga/cover/fake.jpg"}}

    shared_work = {"id": 789, "title": {"english": "Shared work fixture"}, "chapters": 4, "volumes": 2}

    class Provider(BaseHTTPRequestHandler):
        def do_POST(self):
            request = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            variables = request["variables"]
            if not all(field in request["query"] for field in ("coverImage", "genres", "isMediaSpoiler", "staff")):
                response = {"errors": [{"message": "Missing provider fields"}]}
            elif variables == {"title": "Issue 01"}:
                response = {"data": {"Page": {"media": [media_result, no_cover]}}}
            elif variables == {"title": "One Lucky Day"}:
                response = {"data": {"Page": {"media": [no_cover]}}}
            elif variables == {"id": 123}:
                response = {"data": {"Media": media_result}}
            elif variables == {"id": 456}:
                response = {"data": {"Media": no_cover}}
            elif variables == {"id": 789}:
                response = {"data": {"Media": shared_work}}
            elif variables.get("title", "").endswith(" grouping fixture"):
                response = {"data": {"Page": {"media": []}}}
            else:
                response = {"errors": [{"message": "Unexpected test query"}]}
            data = json.dumps(response).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def log_message(self, _format, *_args):
            pass

    password = uuid4().hex
    with ThreadingHTTPServer(("127.0.0.1", 0), Provider) as service:
        server = threading.Thread(target=service.serve_forever, daemon=True)
        server.start()
        try:
            lookup = f"http://127.0.0.1:{service.server_port}/graphql"
            with start_app(executable, fixture / "metadata-data", media, password, metadata_service=lookup) as address:
                wait_for_server(address)
                browser = Browser(address)
                browser.sign_in(password)
                root_id = browser.roots()[0]["id"]
                browser.scan(root_id)
                for _ in range(50):
                    suggestions = json.loads(browser.request("/api/metadata-suggestions", 200))["items"]
                    if not any(row["queued"] for row in suggestions):
                        break
                    time.sleep(0.1)
                assert not any(row["queued"] for row in suggestions)
                (series,) = browser.series(root_id, "?search=Issue+01")["items"]
                url = f"{LIBRARY_ROOTS}/{root_id}/series/{series['key']}"
                result, fallback = json.loads(browser.request(f"{url}/metadata/search?title=Issue+01", 200))
                assert result["providerId"] == 123 and result["kind"] == "doujin" and result["totalChapters"] == 12
                assert result["providerUrl"] == "https://anilist.co/manga/123"
                assert result["summary"] == "Metadata & tags\n\nSecond paragraph"
                assert result["providerDetails"] == {
                    "coverUrl": cover, "format": "ONE_SHOT", "countryOfOrigin": "JP", "startYear": 2011, "endYear": 2012,
                    "genres": ["Drama"], "tags": [{"name": "Doujinshi", "spoiler": False}, {"name": "Hidden outcome", "spoiler": True}],
                    "credits": [{"name": "Fixture Author", "role": "Story & Art"}],
                }
                assert fallback["providerDetails"]["coverUrl"] is None and fallback["providerDetails"]["tags"] == []
                saved = json.loads(browser.request(f"{url}/metadata", 200, method="PUT", token=browser.csrf(), body={"providerId": 123}))
                assert saved == result and json.loads(browser.request(f"{url}/details", 200))["metadata"] == result
                edit = {"title": "My corrected title", "kind": "doujin", "totalChapters": 12}
                assert json.loads(browser.request(f"{url}/metadata/anilist/123", 200)) == result
                corrected = json.loads(browser.request(f"{url}/metadata", 200, method="PUT", token=browser.csrf(),
                    body={**edit, "providerId": 123, "useEditedFields": True, "summary": result["summary"]}))
                assert corrected["title"] == edit["title"] and corrected["providerUrl"] == result["providerUrl"]
                assert corrected["providerDetails"] == result["providerDetails"] and corrected["summary"] == result["summary"]
                assert json.loads(browser.request(f"{url}/details", 200))["metadata"] == corrected
                collections = "/api/library/collections"
                assert json.loads(browser.request(f"{LIBRARY_ROOTS}/{root_id}/tags", 200)) == ["Doujinshi"]
                tagged = browser.series(root_id, "?includeTag=doujinshi")
                assert tagged["total"] == 1, tagged
                assert browser.series(root_id, "?includeTag=Doujinshi&includeTag=Adventure")["total"] == 0
                assert browser.series(root_id, "?search=My+corrected+title&excludeTag=Doujinshi")["total"] == 0
                personal_tags = [{"name": "Doujinshi", "spoiler": False},
                                 {"name": " Slow burn ", "spoiler": False},
                                 {"name": "Quiet ending", "spoiler": False},
                                 {"name": "quiet ending", "spoiler": True}]
                expected_tags = [{"name": "Doujinshi", "spoiler": False},
                                 {"name": "Slow burn", "spoiler": False},
                                 {"name": "Quiet ending", "spoiler": True}]
                original_tags = result["providerDetails"]["tags"]
                change = {**edit, "summary": result["summary"], "tags": personal_tags}
                browser.request(url + "/metadata", 400, method="PUT", body=change)
                browser.request(url + "/metadata", 400, method="PUT", token=browser.csrf(),
                                body={**edit, "tags": [{"name": " ", "spoiler": False}]})
                saved_tags = json.loads(browser.request(url + "/metadata", 200, method="PUT",
                                                       token=browser.csrf(), body=change))
                assert saved_tags["tags"] == expected_tags and saved_tags["hasManualTags"]
                assert saved_tags["providerDetails"]["tags"] == original_tags
                assert json.loads(browser.request(f"{LIBRARY_ROOTS}/{root_id}/tags", 200)) == ["Doujinshi", "Slow burn"]
                assert browser.series(root_id, "?includeTag=slow+burn&view=library")["total"] == 1
                assert browser.series(root_id, "?search=My+corrected+title&excludeTag=Slow+burn&view=library")["total"] == 0
                refreshed = json.loads(browser.request(url + "/metadata", 200, method="PUT", token=browser.csrf(),
                    body={**edit, "providerId": 123, "useEditedFields": True, "summary": result["summary"]}))
                assert refreshed["tags"] == expected_tags
                browser.request(f"{LIBRARY_ROOTS}/{root_id}/series?includeTag=Doujinshi&excludeTag=doujinshi", 400)
                Browser(address).request(collections, 401)
                browser.request(collections, 400, method="POST", body={"name": "Favorites"})
                manual = json.loads(browser.request(collections, 200, method="POST", token=browser.csrf(), body={"name": "Favorites"}))
                dynamic = json.loads(browser.request(collections, 200, method="POST", token=browser.csrf(), body={
                    "name": "Doujin saved search", "filters": {"includeTags": ["doujinshi"], "excludeTags": ["Adventure"]},
                }))
                member_path = f"{collections}/{manual['id']}/membership"
                membership = {"rootId": root_id, "seriesKey": series["key"], "included": True}
                browser.request(member_path, 204, method="PUT", token=browser.csrf(), body=membership)
                browser.request(member_path, 204, method="PUT", token=browser.csrf(), body=membership)
                browser.request(f"{collections}/{dynamic['id']}/membership", 404, method="PUT", token=browser.csrf(), body=membership)
                with ZipFile(media / "primary" / "One Lucky Day.cbz", "w") as archive:
                    archive.writestr("01.png", synthetic_png(45, 125, 60))
                browser.scan(root_id)
                (one_shot,) = browser.series(root_id, "?search=One+Lucky+Day")["items"]
                single = f"{LIBRARY_ROOTS}/{root_id}/series/{one_shot['key']}"
                assert json.loads(browser.request(single + "/chapters", 200))["total"] == 0
                for _ in range(80):
                    (proposal,) = json.loads(browser.request("/api/metadata-suggestions?search=One+Lucky+Day", 200))["items"]
                    if proposal["candidates"]:
                        break
                    time.sleep(0.1)
                assert proposal["candidates"] and proposal["candidates"][0]["match"]["providerId"] == 456
                browser.request(f"/api/metadata-suggestions/{root_id}/{one_shot['key']}", 200, method="POST", token=browser.csrf(),
                                body={"providerId": 456, "revision": proposal["revision"]})
                (item,) = json.loads(browser.request(single + "/chapters?published=true", 200))["items"]
                assert item["chapter"] == "1" and item["contentId"] and item["readableFiles"] == 1
                assert json.loads(browser.request(single + "/chapter-releases?chapter=1&published=true", 200))[0]["contentId"] == item["contentId"]
                verify_same_provider_group(browser, root_id, media / "primary")
            with start_app(executable, fixture / "metadata-data", media, None) as address:
                wait_for_server(address)
                browser = Browser(address)
                browser.sign_in(password)
                for collection in (manual, dynamic):
                    items = json.loads(browser.request(f"{collections}/{collection['id']}/series", 200))["items"]
                    assert [(item["rootId"], item["key"]) for item in items] == [(root_id, series["key"])]
                browser.request(member_path, 204, method="PUT", token=browser.csrf(), body=dict(membership, included=False))
                assert json.loads(browser.request(f"{collections}/{manual['id']}/series", 200))["total"] == 0
                browser.request(f"{collections}/{manual['id']}", 204, method="DELETE", token=browser.csrf())
                assert browser.series(root_id, "?includeTag=doujinshi")["total"] == 1
                stored = json.loads(browser.request(url + "/details?published=true", 200))["metadata"]
                assert stored["tags"] == expected_tags
                assert json.loads(browser.request(f"{collections}/tags", 200)) == ["Doujinshi", "Slow burn"]
                cleared = json.loads(browser.request(url + "/metadata?published=true", 200, method="PUT", token=browser.csrf(),
                    body={**edit, "tags": []}))
                assert cleared["tags"] == [] and cleared["hasManualTags"]
                restored = json.loads(browser.request(url + "/metadata?published=true", 200, method="PUT", token=browser.csrf(),
                    body={**edit, "useProviderTags": True}))
                assert restored["tags"] == original_tags and not restored.get("hasManualTags")
        finally:
            service.shutdown()
            server.join(timeout=5)


def verify_same_provider_group(browser, root, path):
    first = "Western grouping fixture Ch.1.cbz"
    second = "Eastern grouping fixture Vol.2 Ch.2.cbz"
    for index, name in enumerate((first, second)):
        with ZipFile(path / name, "w") as archive:
            archive.writestr("01.png", synthetic_png(75 + index, 130, 100))
    browser.scan(root)
    indexed = {item["relativePath"]: item for item in browser.files(root, "?search=grouping+fixture")["items"]}
    keys = [indexed[name]["seriesKey"] for name in (first, second)]
    assert keys[0] != keys[1]
    urls = [f"{LIBRARY_ROOTS}/{root}/series/{key}" for key in keys]
    progress = {"chapter": "2", "volume": "2", "completed": True}
    browser.request(urls[1] + "/chapter-progress", 204, method="PUT", token=browser.csrf(), body=progress)
    browser.request(urls[0] + "/metadata", 200, method="PUT", token=browser.csrf(), body={"providerId": 789})
    customized = {"title": "Shared work fixture revised", "summary": "Preserved catalog description", "kind": "manga",
                  "totalChapters": 4, "totalVolumes": 2}
    saved = json.loads(browser.request(urls[0] + "/metadata", 200, method="PUT", token=browser.csrf(), body=customized))
    attached = json.loads(browser.request(urls[1] + "/metadata", 200, method="PUT", token=browser.csrf(), body={"providerId": 789}))
    assert attached == saved and attached["providerId"] == 789
    picker = json.loads(browser.request("/api/file-associations/series?search=Shared+work+fixture&limit=1", 200))
    assert picker["total"] == 1 and picker["items"][0]["title"] == customized["title"]
    (card,) = browser.series(root, "?view=library&search=Shared+work+fixture")["items"]
    assert card["fileCount"] == 2 and card["key"] in keys and card["hasCover"]
    browser.request(f"{LIBRARY_ROOTS}/{root}/series/{card['coverSeriesKey']}/cover", 200)
    for alias in ("Eastern", "Western"):
        assert browser.series(root, "?view=library&search=" + alias)["items"] == [card]
    for key, url in zip(keys, urls):
        assert browser.series(root, "?view=library&seriesKey=" + key)["items"] == [card]
        files = browser.files(root, "?view=library&seriesKey=" + key)["items"]
        assert {file["relativePath"] for file in files} == {first, second}
        volume = browser.files(root, "?view=library&seriesKey=" + key + "&volume=2")["items"]
        assert [file["relativePath"] for file in volume] == [second]
        details = json.loads(browser.request(url + "/details?published=true", 200))
        assert details["availableChapters"] == 2 and details["readCatalogChapters"] == 1
        chapters = json.loads(browser.request(url + "/chapters?published=true", 200))["items"]
        assert {(row["chapter"], row["completed"]) for row in chapters if not row.get("missing")} == {("1", False), ("2", True)}
        releases = json.loads(browser.request(url + "/chapter-releases?volume=2&chapter=2&published=true", 200))
        assert [release["relativePath"] for release in releases] == [second]
    browser.request(urls[0] + "/chapter-progress?published=true", 204, method="PUT",
                    token=browser.csrf(), body={**progress, "completed": False})
    details = json.loads(browser.request(urls[1] + "/details?published=true", 200))
    assert details["readCatalogChapters"] == 0 and details["readChapters"] == 0

    other_file = next(file for file in indexed.values() if file["seriesKey"] != card["key"])
    canonical = f"{LIBRARY_ROOTS}/{root}/series/{card['key']}"
    identity = other_file["contentId"]
    coverage = canonical + "/coverage"
    browser.request(f"{coverage}?contentId={identity}", 404)
    assert json.loads(browser.request(f"{coverage}?contentId={identity}&published=true", 200)) == []
    change = {"contentId": identity, "volume": "7", "chapters": ["5", "5.5"]}
    browser.request(coverage + "?published=true", 400, method="PUT", body=change)
    browser.request(coverage + "?published=true", 204, method="PUT", token=browser.csrf(), body=change)
    (assigned,) = browser.files(root, f"?view=library&seriesKey={card['key']}&volume=7")["items"]
    assert assigned["contentId"] == identity and assigned["volume"] == "7"
    if other_file["volume"]:
        assert browser.files(root, f"?view=library&seriesKey={card['key']}&volume={other_file['volume']}")["total"] == 0
    volumes = json.loads(browser.request(canonical + "/volume-releases?published=true&volume=7", 200))
    assert [release["contentId"] for release in volumes] == [identity]
    for label in ("5", "5.5"):
        releases = json.loads(browser.request(canonical + f"/chapter-releases?published=true&volume=7&chapter={label}", 200))
        assert [release["contentId"] for release in releases] == [identity]

    chapter_url = "/release-metadata?volume=7&chapter=5.5"
    volume_url = "/release-metadata?volume=7"
    chapter_details = {"title": "The Turning Point", "summary": "Kept once for all releases."}
    volume_details = {"title": "Collected edition", "summary": None}
    browser.request(urls[0] + chapter_url, 400, method="PUT", body=chapter_details)
    browser.request(urls[0] + chapter_url, 204, method="PUT", token=browser.csrf(), body=chapter_details)
    browser.request(urls[1] + volume_url, 204, method="PUT", token=browser.csrf(), body=volume_details)
    assert json.loads(browser.request(urls[1] + chapter_url, 200)) == chapter_details
    assert json.loads(browser.request(urls[0] + volume_url, 200)) == volume_details
    matching = json.loads(browser.request(urls[1] + "/chapters?published=true&search=turning", 200))
    assert matching["total"] == 1 and matching["items"][0]["title"] == chapter_details["title"]
    details = json.loads(browser.request(urls[0] + "/details?published=true", 200))
    assert details["volumeTitles"] == {"7": volume_details["title"]}

    choices = json.loads(browser.request(canonical + "/cover-options?published=true&limit=1", 200))
    assert choices["total"] == 2 and len(choices["items"]) == 1
    page = json.loads(browser.request(canonical + "/cover-options?published=true&limit=1&offset=1", 200))
    chosen = page["items"][0]
    assert choices["items"][0]["id"] != chosen["id"]
    image_url = canonical + f"/cover-options/{chosen['id']}?published=true"
    selected = browser.request(image_url, 200)
    browser.request(canonical + "/cover-preference?published=true", 204, method="PUT", token=browser.csrf(),
                    body={"candidateId": chosen["id"]})
    assert all(browser.request(url + "/cover", 200) == selected for url in urls)
    browser.scan(root)
    assert browser.request(canonical + "/cover", 200) == selected
    upload = canonical + "/cover-upload?published=true"
    image = synthetic_png(68, 90, 150, width=31, height=47)
    browser.request(upload, 400, method="PUT", body=image)
    browser.request(upload, 400, method="PUT", token=browser.csrf(), body=b"Not an image" * 4)
    browser.request(upload, 204, method="PUT", token=browser.csrf(), body=image)
    for url in urls:
        assert webp_dimensions(browser.request(url + "/cover", 200)) == (31, 47)
    browser.request("/api/maintenance/artwork-cache/clear", 200, method="POST", token=browser.csrf())
    assert webp_dimensions(browser.request(canonical + "/cover", 200)) == (31, 47)
    browser.scan(root)
    browser.request(canonical + "/cover-preference?published=true", 204, method="DELETE", token=browser.csrf())
    options = json.loads(browser.request(canonical + "/cover-options?published=true", 200))
    assert options["preferredLabel"] is None and webp_dimensions(browser.request(canonical + "/cover", 200)) != (31, 47)

    new = path / "Separate attachment fixture Ch.9.cbz"
    with ZipFile(new, "w") as archive:
        archive.writestr("01.png", synthetic_png(9, 90, 150))
    browser.scan(root)
    (new_file,) = browser.files(root, "?search=Separate+attachment+fixture")["items"]
    association = f"/api/file-associations/{root}/files/{new_file['id']}"
    args = {"contentId": new_file["contentId"], "seriesKey": new_file["seriesKey"], "metadataId": picker["items"][0]["id"]}
    preview = json.loads(browser.request(association + "/preview", 200, method="POST", token=browser.csrf(), body=args))
    browser.request(association, 204, method="PUT", token=browser.csrf(), body=dict(args, revision=preview["revision"]))
    browser.scan(root)
    (card,) = browser.series(root, "?view=library&search=Shared+work+fixture")["items"]
    assert card["fileCount"] == 3
    assert json.loads(browser.request(canonical + "/details?published=true", 200))["metadata"] == saved

    secondary = next(item["id"] for item in browser.roots() if item["id"] != root)
    name = "Remote folder fixture Ch.10.cbz"
    with ZipFile(path.parent / "archive" / name, "w") as archive:
        archive.writestr("01.png", synthetic_png(47, 90, 101))
    browser.scan(secondary)
    (peer,) = browser.series(secondary, "?search=Remote+folder+fixture")["items"]
    peer_url = f"{LIBRARY_ROOTS}/{secondary}/series/{peer['key']}"
    assert json.loads(browser.request(peer_url + "/metadata", 200, method="PUT", token=browser.csrf(),
                                      body={"providerId": 789})) == saved
    assert json.loads(browser.request(peer_url + chapter_url, 200)) == chapter_details
    gallery = json.loads(browser.request(canonical + "/cover-options?published=true", 200))
    (remote_cover,) = [item for item in gallery["items"] if item["label"] == name]
    browser.request(canonical + "/cover-preference?published=true", 204, method="PUT", token=browser.csrf(),
                    body={"candidateId": remote_cover["id"]})
    assert browser.request(canonical + "/cover", 200) == browser.request(peer_url + "/cover", 200)


def verify_reader(owner, peer, root_id, path):
    for edition in ("Four", "Five"):
        (rar,) = owner.files(root_id, "?" + urlencode({"search": "Archive coverage " + edition}))["items"]
        url = f"/api/reader/{rar['contentId']}"
        assert json.loads(owner.request(url, 200))["pageCount"] == 3
        assert owner.request(url + "/pages/0", 200) == synthetic_png(10, 12, 65)
        assert owner.request(url + "/pages/1", 200) == synthetic_png(14, 90, 180, 19, 33)

    series = owner.series(root_id, "?search=eclipse")["items"][0]
    details = f"{LIBRARY_ROOTS}/{root_id}/series/{series['key']}"
    (archive,) = [file for file in owner.files(root_id, "?" + urlencode({"seriesKey": series["key"]}))["items"]
                  if file["relativePath"] == "Éclipse/Éclipse v01.cbz"]
    identity = archive["contentId"]
    book_url = f"/api/reader/{identity}"
    Browser(owner.url).request(book_url, 401)
    Browser(owner.url).request(f"{book_url}/pages/0", 401)
    book = json.loads(owner.request(book_url, 200))
    assert book["pageCount"] == 2 and book["progress"]["revision"] == 0
    assert owner.request(f"{book_url}/pages/0", 200) == synthetic_png(32, 160, 122)
    assert owner.request(f"{book_url}/pages/1", 200) == synthetic_png(221, 42, 70, 26, 39)
    owner.request(f"{book_url}/pages/2", 404)
    owner.request("/api/reader/resume", 204)

    local = {"title": "Éclipse", "kind": "manga", "totalChapters": 4}
    owner.request(f"{details}/metadata", 200, method="PUT", body=local, token=owner.csrf())
    owner.request(f"{details}/coverage", 204, method="PUT", token=owner.csrf(),
                  body={"contentId": identity, "volume": "1", "chapters": ["1", "2"]})
    first_update = {"updateId": str(uuid4()), "baseRevision": 0, "pageIndex": 1}
    owner.request(f"{book_url}/progress", 400, method="PUT", body=first_update)
    result = json.loads(owner.request(f"{book_url}/progress", 200, method="PUT", token=owner.csrf(), body=first_update))
    assert result == {"pageIndex": 1, "revision": 1, "completed": False}
    assert json.loads(peer.request(book_url, 200))["progress"] == result
    series_reading = json.loads(peer.request(f"{details}/details", 200))["reading"]
    assert series_reading["contentId"] == identity and (series_reading["pageIndex"], series_reading["pageCount"]) == (1, 2)
    assert json.loads(peer.request("/api/reader/resume", 200))["contentId"] == identity
    assert json.loads(owner.request(f"{book_url}/progress", 200, method="PUT", token=owner.csrf(), body=first_update)) == result
    conflict = dict(first_update, updateId=str(uuid4()), pageIndex=0)
    assert json.loads(peer.request(f"{book_url}/progress", 409, method="PUT", token=peer.csrf(), body=conflict)) == result
    back = {"updateId": str(uuid4()), "baseRevision": 1, "pageIndex": 0}
    assert json.loads(peer.request(f"{book_url}/progress", 200, method="PUT", token=peer.csrf(), body=back))["pageIndex"] == 0
    finish = {"updateId": str(uuid4()), "baseRevision": 2, "pageIndex": 1, "completed": True}
    result = json.loads(peer.request(f"{book_url}/progress", 200, method="PUT", token=peer.csrf(), body=finish))
    assert result["completed"] and result["revision"] == 3
    assert json.loads(owner.request(f"{details}/details", 200))["readChapters"] == 2
    owner.request("/api/reader/resume", 204)

    source = path / archive["relativePath"]
    # The artwork test uses an exact copy. Leave one location for the replacement test.
    source.with_name("Éclipse v04.cbz").unlink()
    moved = source.with_name("Relocated volume.cbz")
    source.rename(moved)
    owner.scan(root_id)
    assert json.loads(owner.request(book_url, 200))["progress"] == result
    with ZipFile(moved, "w") as replacement:
        replacement.writestr("1.png", synthetic_png(1, 2, 3))
    owner.scan(root_id)
    owner.request(book_url, 422)
    (new_file,) = owner.files(root_id, "?search=Relocated+volume")["items"]
    new_book = json.loads(owner.request("/api/reader/" + new_file["contentId"], 200))
    assert new_book["progress"]["revision"] == 0 and new_file["contentId"] != identity


def supports_quick_scan(path):
    if os.name == "nt":
        import ctypes

        kernel = ctypes.windll.kernel32
        volume = ctypes.create_unicode_buffer(512)
        filesystem = ctypes.create_unicode_buffer(16)
        return bool(kernel.GetVolumePathNameW(str(path), volume, len(volume))
                    and kernel.GetDriveTypeW(volume) == 3
                    and kernel.GetVolumeInformationW(volume, None, 0, None, None, None, filesystem, len(filesystem))
                    and filesystem.value == "NTFS")

    types = {"ef53", "58465342", "9123683e", "1021994"}
    result = subprocess.run(["stat", "-f", "-c", "%t", path], check=True, text=True, capture_output=True)
    return Path("/proc/sys/kernel/random/boot_id").is_file() and result.stdout.strip() in types


def scan_library(owner, media):
    primary, archive = owner.roots()
    assert primary["state"] == archive["state"] == "idle"
    root_id, archive_id = primary["id"], archive["id"]
    assert owner.files(root_id) == {"total": 0, "items": []}
    owner.request(f"{LIBRARY_ROOTS}/{root_id}/scan", 400, method="POST")
    owner.request(f"{LIBRARY_ROOTS}/{uuid4()}/scan", 404, method="POST", token=owner.csrf())
    owner.request(f"{LIBRARY_ROOTS}/{root_id}/files?limit=101", 400)

    primary_path = media / "primary"
    chapter = primary_path / "nested" / "Chapter 12.5.CBZ"
    before = (chapter.read_bytes(), chapter.stat().st_mtime_ns)
    first_scan = owner.scan(root_id)
    assert first_scan["fileCount"] == 53 and first_scan["lastScannedAt"]
    assert first_scan["skippedLinks"] == (1 if os.name == "nt" else 2)
    cold = first_scan["lastScan"]
    assert not cold["full"] and cold["hashedFiles"] == 53 and cold["reusedFiles"] == 0 and cold["bytesRead"] > 0
    warm = owner.scan(root_id)["lastScan"]
    reuse = supports_quick_scan(primary_path)
    assert not warm["full"] and warm["reusedFiles"] == (53 if reuse else 0), warm
    assert warm["hashedFiles"] == (0 if reuse else 53), warm
    assert warm["bytesRead"] == (0 if reuse else cold["bytesRead"]), warm
    first_scan = owner.scan(root_id, full=True)
    full = first_scan["lastScan"]
    assert full["full"] and full["hashedFiles"] == 53 and full["reusedFiles"] == 0
    assert full["bytesRead"] == cold["bytesRead"] and full["elapsedMilliseconds"] >= 0
    first_page = owner.files(root_id)
    final_page = owner.files(root_id, "?offset=50")
    assert first_page["total"] == final_page["total"] == 53
    assert len(first_page["items"]) == 50 and len(final_page["items"]) == 3
    (chapter_row,) = (row for row in final_page["items"] if row["relativePath"] == "nested/Chapter 12.5.CBZ")
    assert chapter_row["extension"] == "cbz" and chapter_row["size"] == len(before[0]) and chapter_row["modifiedAt"]

    assert owner.scan(archive_id)["fileCount"] == 1
    assert owner.files(archive_id)["items"][0]["relativePath"] == "Volume.epub"
    assert (chapter.read_bytes(), chapter.stat().st_mtime_ns) == before

    offline_path = media / "offline"
    primary_path.rename(offline_path)
    assert owner.scan(root_id, "unavailable")["lastScannedAt"] == first_scan["lastScannedAt"]
    assert owner.files(root_id) == first_page
    primary_path.mkdir()
    assert owner.scan(root_id, "emptyReview")["fileCount"] == 53
    assert owner.files(root_id) == first_page
    primary_path.rmdir()
    offline_path.rename(primary_path)

    (primary_path / "Issue 00.pdf").unlink()
    assert owner.scan(root_id)["fileCount"] == 52
    assert owner.files(root_id, "?offset=50")["items"][-1]["id"] == chapter_row["id"]
    assert (chapter.read_bytes(), chapter.stat().st_mtime_ns) == before


def verify_file_identity(owner, media):
    primary, archive = owner.roots()
    source = media / "primary" / "nested" / "Chapter 12.5.CBZ"
    source_bytes = source.read_bytes()

    def entries(root):
        return {row["relativePath"]: row for row in owner.files(root["id"], "?limit=100")["items"]}

    chapter_path = "nested/Chapter 12.5.CBZ"
    original = entries(primary)[chapter_path]
    identity = original["contentId"]
    assert identity and original["sha256"] == sha256(source_bytes).hexdigest() and original["indexedLocations"] == 1

    copy = media / "archive" / "Copy.cbz"
    alias = media / "archive" / "Alias.cbz"
    copy.write_bytes(source_bytes)
    os.link(source, alias)
    owner.scan(archive["id"])
    locations = [entries(primary)[chapter_path], entries(archive)[copy.name], entries(archive)[alias.name]]
    assert len({row["id"] for row in locations}) == 3
    assert all(row["contentId"] == identity and row["indexedLocations"] == 3 for row in locations)
    copy.unlink()
    alias.unlink()
    owner.scan(archive["id"])

    renamed = source.with_name("Renamed.cbz")
    source.rename(renamed)
    owner.scan(primary["id"])
    after_rename = entries(primary)
    assert chapter_path not in after_rename
    assert after_rename["nested/Renamed.cbz"]["contentId"] == identity

    moved = media / "archive" / renamed.name
    renamed.rename(moved)
    owner.scan(primary["id"])
    assert "nested/Renamed.cbz" not in entries(primary)
    owner.scan(archive["id"])
    after_move = entries(archive)[moved.name]
    assert after_move["contentId"] == identity and after_move["indexedLocations"] == 1

    previous = moved.stat()
    replacement = b"!" + source_bytes[1:]
    moved.write_bytes(replacement)
    os.utime(moved, ns=(previous.st_atime_ns, previous.st_mtime_ns))
    changed_scan = owner.scan(archive["id"])["lastScan"]
    assert changed_scan["hashedFiles"] >= 1
    if supports_quick_scan(moved.parent):
        assert changed_scan["hashedFiles"] == 1 and changed_scan["reusedFiles"] == 1
        assert changed_scan["bytesRead"] == len(replacement)
    replaced = entries(archive)[moved.name]
    assert replaced["id"] == after_move["id"] and replaced["contentId"] != identity
    assert replaced["sha256"] == sha256(replacement).hexdigest()

    source.write_bytes(source_bytes)
    moved.unlink()
    owner.scan(archive["id"])
    if hasattr(os, "mkfifo"):
        special = source.with_name("Pipe.cbz")
        os.mkfifo(special)
        owner.scan(primary["id"])
        unreadable = entries(primary)["nested/Pipe.cbz"]
        assert unreadable["contentId"] is None and unreadable["sha256"] is None and unreadable["indexedLocations"] is None
        special.unlink()

    owner.scan(primary["id"])
    assert entries(primary)[chapter_path]["contentId"] == identity


def verify_volume_chapters(owner, root_id, path):
    editions = path / "Collected editions"
    editions.mkdir()
    range_file = editions / "Collected editions Vol.02 Ch.3–5.cbz"
    archive_file = editions / "Collected editions Vol.03.cbz"
    ambiguous_file = editions / "Collected editions Vol.04 Ch.13-17.cbz"
    unnamed_file = editions / "Collected editions Vol.05.cbz"
    standalone = editions / "Collected editions Vol.02 Ch.3.cbz"
    examples = [(range_file, ["1.png", "2.png"]),
                (archive_file, ["cover.png", "Chapter 6 - Beginnings/1.png", "Chapter 7.5/1.png"]),
                (ambiguous_file, ["Chapter 13/1.png", "Chapter 17/1.png"]),
                (unnamed_file, ["001.png", "002.png"]), (standalone, ["1.png"])]
    for index, (archive_file_path, pages) in enumerate(examples):
        with ZipFile(archive_file_path, "w") as archive:
            for page in pages:
                archive.writestr(page, synthetic_png(1, 2, index + 3))
    owner.scan(root_id)
    (group,) = owner.series(root_id, "?search=Collected+editions")["items"]
    group_url = f"{LIBRARY_ROOTS}/{root_id}/series/{group['key']}"
    files = {Path(item["relativePath"]).name: item for item in owner.files(root_id, "?seriesKey=" + group["key"])["items"]}

    def coverage(file, url=group_url):
        content_id = files[file.name]["contentId"]
        return json.loads(owner.request(f"{url}/coverage?contentId={content_id}", 200))

    assert [(item["volume"], item["chapter"], item["source"]) for item in coverage(range_file)] == [
        ("2", "3", "filename"), ("2", "4", "filename"), ("2", "5", "filename"),
    ]
    assert [(item["chapter"], item["source"]) for item in coverage(archive_file)] == [("6", "archive"), ("7.5", "archive")]
    assert coverage(ambiguous_file) == coverage(unnamed_file) == []
    details = json.loads(owner.request(f"{group_url}/details", 200))
    assert details["availableChapters"] == 5
    assert details["indexedVolumes"] == len(details["volumes"])
    assert "2" in details["readableVolumes"] and "5" in details["readableVolumes"]
    volume_query = "?seriesKey=" + group["key"] + "&volume=02&limit=1"
    first_volume_page = owner.files(root_id, volume_query)
    second_volume_page = owner.files(root_id, volume_query + "&offset=1")
    assert first_volume_page["total"] == second_volume_page["total"] == 2
    assert {Path(page["items"][0]["relativePath"]).name for page in (first_volume_page, second_volume_page)} == {range_file.name, standalone.name}
    assert owner.files(root_id, "?seriesKey=" + group["key"] + "&volume=9") == {"total": 0, "items": []}
    owner.request(f"{LIBRARY_ROOTS}/{root_id}/files?volume=2", 400)
    owner.request(f"{LIBRARY_ROOTS}/{root_id}/files?seriesKey={group['key']}&volume=unknown", 400)
    volume_two = json.loads(owner.request(f"{group_url}/volume-releases?volume=02", 200))
    assert {Path(file["relativePath"]).name for file in volume_two} == {range_file.name}
    assert json.loads(owner.request(f"{group_url}/volume-releases?volume=9", 200)) == []
    releases = json.loads(owner.request(f"{group_url}/chapter-releases?volume=2&chapter=3", 200))
    assert {item["contentId"] for item in releases} == {files[range_file.name]["contentId"], files[standalone.name]["contentId"]}
    preferred_id = files[standalone.name]["contentId"]
    preference = {"volume": "02", "chapter": "03", "contentId": preferred_id}
    owner.request(f"{group_url}/chapter-preference", 404, method="PUT", token=owner.csrf(), body=dict(preference, chapter="4"))
    owner.request(f"{group_url}/chapter-preference", 204, method="PUT", token=owner.csrf(), body=preference)
    preferred = json.loads(owner.request(f"{group_url}/chapter-releases?volume=2&chapter=3", 200))
    assert preferred[0]["contentId"] == preferred_id and preferred[0]["preferred"] and not preferred[1]["preferred"]
    assert json.loads(owner.request(f"{group_url}/details", 200))["chapters"][0]["preferredContentId"] == preferred_id
    owner.request(f"{group_url}/chapter-preference", 204, method="PUT", token=owner.csrf(), body=dict(preference, contentId=None))
    mark = {"volume": "02", "chapter": "03", "completed": True}
    owner.request(f"{group_url}/chapter-progress", 400, method="PUT", body=mark)
    owner.request(f"{group_url}/chapter-progress", 204, method="PUT", token=owner.csrf(), body=mark)
    marked = json.loads(owner.request(f"{group_url}/details", 200))
    assert marked["readChapters"] == 1 and marked["chapters"][0]["completed"]
    assert all(item["pageIndex"] is None for item in json.loads(owner.request(f"{group_url}/chapter-releases?volume=2&chapter=3", 200)))
    owner.request(f"{group_url}/chapter-progress", 204, method="PUT", token=owner.csrf(), body=dict(mark, completed=False))
    assert json.loads(owner.request(f"{group_url}/details", 200))["readChapters"] == 0

    candidates = json.loads(owner.request(f"{group_url}/volume-releases?volume=5", 200))
    assert len(candidates) == 1 and candidates[0]["unresolvedVolume"]
    volume_five = owner.files(root_id, "?seriesKey=" + group["key"] + "&volume=5")
    assert volume_five["total"] == 1 and volume_five["items"][0]["contentId"] == candidates[0]["contentId"]
    assert json.loads(owner.request(f"/api/reader/{candidates[0]['contentId']}", 200))["pageCount"] == 2
    manifest = {"edition": "Fixture digital edition", "volume": "5", "chapters": ["21", "22"],
                "contentIds": [files[unnamed_file.name]["contentId"]]}
    owner.request(f"{group_url}/edition-volumes", 204, method="PUT", token=owner.csrf(), body=manifest)
    (unknown,) = json.loads(owner.request(f"{group_url}/edition-volumes", 200))
    assert unknown["state"] == "unknown"
    verified = dict(manifest, fullVolumeContentId=files[unnamed_file.name]["contentId"])
    owner.request(f"{group_url}/edition-volumes", 204, method="PUT", token=owner.csrf(), body=verified)
    (complete,) = json.loads(owner.request(f"{group_url}/edition-volumes", 200))
    assert complete["id"] == unknown["id"] and complete["state"] == "complete" and complete["available"] == ["21", "22"]
    owner.request(f"{group_url}/edition-volumes", 409, method="PUT", token=owner.csrf(), body=verified)
    manual = {"contentId": files[range_file.name]["contentId"], "volume": "2", "chapters": ["9"]}
    owner.request(f"{group_url}/coverage", 204, method="PUT", token=owner.csrf(), body=manual)
    assert coverage(range_file) == [{"volume": "2", "chapter": "9"}]
    owner.request(f"{group_url}/coverage", 204, method="PUT", token=owner.csrf(), body=dict(manual, chapters=[]))
    assert len(coverage(range_file)) == 3

    moved = editions / "Collection renamed without chapter labels.cbz"
    range_file.rename(moved)
    owner.scan(root_id)
    (renamed,) = owner.series(root_id, "?search=Collection+renamed")["items"]
    files[moved.name] = files[range_file.name]
    new_url = f"{LIBRARY_ROOTS}/{root_id}/series/{renamed['key']}"
    assert len(coverage(moved, new_url)) == 3


def verify_catalog_visibility(owner, root, path):
    def comic(name, color):
        with ZipFile(path / name, "w") as archive:
            archive.writestr("01.png", synthetic_png(*color))

    def files(view, *params):
        response = owner.files(root, "?view=" + view + "".join("&" + param for param in params))
        return response["total"], {item["relativePath"]: item for item in response["items"]}

    def group(search):
        return owner.series(root, "?search=" + search)["items"][0]["key"]

    first = "Published Parchment Vol.01 Ch.01.cbz"
    later = "Published Parchment Vol.02 Ch.02.cbz"
    no_cover = "Unillustrated Periodical Vol.01.pdf"
    pending = "Pending Signal Ch.01.cbz"
    comic(first, (90, 120, 150))
    comic(pending, (90, 121, 150))
    duplicate = "Pending Signal Ch.01 [Exact Copy].cbz"
    (path / duplicate).write_bytes((path / pending).read_bytes())
    comic("03.cbz", (90, 122, 150))
    (path / no_cover).write_bytes(b"Unillustrated periodical fixture")
    owner.scan(root)
    parchment = group("Published+Parchment")
    url = f"{LIBRARY_ROOTS}/{root}/series/{parchment}"
    assert files("library", f"seriesKey={parchment}")[0] == 0
    assert first in files("attention", "search=Published+Parchment")[1]
    assert pending in files("association", "search=Pending+Signal")[1]
    ordinary = files("association", "search=Pending+Signal")[1][pending]
    version = f"/api/versions/{root}/files/{ordinary['id']}"
    change = {"contentId": ordinary["contentId"], "revision": 0, "language": "ja",
              "color": "black-and-white", "censorship": "uncensored"}
    owner.request(version, 400, method="PUT", body=change)
    owner.request(version, 204, method="PUT", token=owner.csrf(), body=change)
    pending_files = files("all", "search=Pending+Signal")[1]
    ungrouped = files("all", "search=03.cbz")[1]["03.cbz"]
    verify_bulk_release_tags(owner, root, [pending_files[pending], pending_files[duplicate], ungrouped])
    assert "03.cbz" in files("series", "search=03.cbz")[1]
    assert pending in files("metadata", "search=Pending+Signal")[1]
    owner.request(url + "/details?published=true", 404)

    metadata = {"title": "Published Parchment", "alternateTitle": "Opposite Title", "kind": "manga", "totalChapters": 9}
    owner.request(url + "/metadata", 200, method="PUT", token=owner.csrf(), body=metadata)
    no_cover_url = f"{LIBRARY_ROOTS}/{root}/series/{group('Unillustrated+Periodical')}"
    owner.request(no_cover_url + "/metadata", 200, method="PUT", token=owner.csrf(),
                  body={"title": "Unillustrated Periodical", "kind": "manga"})
    assert first not in files("attention", "search=Published+Parchment")[1]
    assert no_cover in files("library", "search=Unillustrated+Periodical")[1]
    assert no_cover in files("cover", "search=Unillustrated+Periodical")[1]
    assert no_cover in files("attention", "search=Unillustrated+Periodical")[1]
    assert files("library", "search=Unillustrated+Periodical")[1][no_cover]["id"] == files("all", "search=Unillustrated+Periodical")[1][no_cover]["id"]

    comic(later, (91, 120, 150))
    owner.scan(root)
    owner.request(url + "/metadata?published=true", 200, method="PUT", token=owner.csrf(), body={**metadata, "totalChapters": 10})
    assert files("library", f"seriesKey={parchment}", "limit=1")[0] == 1
    assert owner.series(root, f"?seriesKey={parchment}&view=library")["items"][0]["fileCount"] == 1
    assert owner.series(root, f"?seriesKey={parchment}&view=attention")["items"][0]["fileCount"] == 1
    total, attention = files("attention", f"seriesKey={parchment}")
    assert total == 1 and set(attention) == {later}
    assert files("metadata", f"seriesKey={parchment}")[0] == 0
    details = json.loads(owner.request(url + "/details?published=true", 200))
    assert details["availableChapters"] == 1 and details["volumes"] == ["1"] and details["metadata"]["totalChapters"] == 10
    chapter = json.loads(owner.request(url + "/chapter-releases?published=true&volume=2&chapter=2", 200))
    assert chapter == []
    assert json.loads(owner.request(url + "/volume-releases?published=true&volume=2", 200)) == []

    release = attention[later]
    confirmation = {"fileId": release["id"], "contentId": release["contentId"]}
    owner.request(url + "/association", 400, method="POST", body=confirmation)
    owner.request(url + "/association", 409, method="POST", token=owner.csrf(), body={**confirmation, "contentId": uuid4().hex})
    owner.request(url + "/association", 204, method="POST", token=owner.csrf(), body=confirmation)
    owner.request(url + "/association", 204, method="POST", token=owner.csrf(), body=confirmation)
    total, page = files("library", f"seriesKey={parchment}", "limit=1", "offset=1")
    assert total == 2 and page[later]["id"] == release["id"]
    assert page[later]["associationConfirmed"]
    assert files("attention", f"seriesKey={parchment}")[0] == 0
    assert json.loads(owner.request(version, 200))["tags"] == {
        "language": "ja", "color": "black-and-white", "censorship": "uncensored",
        "edition": None, "group": "Batch Fixture", "revision": 2,
    }
    catalog_url = url + "/chapters?published=true"
    chapter_page = json.loads(owner.request(catalog_url + "&offset=2&limit=2", 200))
    assert chapter_page["total"] == 10 and [item["chapter"] for item in chapter_page["items"]] == ["3", "4"]
    assert all(item["missing"] and item["readableFiles"] == 0 and item["contentId"] is None for item in chapter_page["items"])
    owner.request(url + "/chapter-progress", 204, method="PUT", token=owner.csrf(),
                  body={"volume": None, "chapter": "4", "completed": True})
    (chapter_four,) = json.loads(owner.request(catalog_url + "&search=4", 200))["items"]
    assert chapter_four["completed"] and chapter_four["missing"]
    owner.request(url + "/chapter-progress", 404, method="PUT", token=owner.csrf(),
                  body={"volume": None, "chapter": "11", "completed": True})
    owner.request(url + "/edition-volumes", 204, method="PUT", token=owner.csrf(),
                  body={"edition": "Default", "volume": "4", "chapters": ["4", "5"], "contentIds": []})
    volume_chapters = json.loads(owner.request(catalog_url + "&volume=4", 200))["items"]
    assert [item["chapter"] for item in volume_chapters] == ["4", "5"]
    assert volume_chapters[0]["completed"] and all(item["missing"] for item in volume_chapters)
    details = json.loads(owner.request(url + "/details?published=true", 200))
    assert details["readCatalogChapters"] == 1 and "4" in details["volumes"] and "4" not in details["readableVolumes"]
    verify_file_association(owner, root, path, pending, ordinary, parchment, version)


def verify_intake_file_split(owner, root, path):
    kept = path / "Split Fixture Vol.01.pdf"
    outlier = path / "Split Fixture Vol.02.pdf"
    alias = path / "Different Proposed Group Vol.01.pdf"
    kept.write_bytes(b"Split fixture keep")
    outlier.write_bytes(b"Split fixture outlier")
    alias.write_bytes(kept.read_bytes())
    owner.scan(root)
    (group,) = owner.series(root, "?search=Split+Fixture")["items"]
    group_url = f"{LIBRARY_ROOTS}/{root}/series/{group['key']}"
    before = {file["relativePath"]: file for file in owner.files(root, f"?seriesKey={group['key']}")["items"]}
    assert len(before) == 2
    owner.request(group_url + "/metadata", 200, method="PUT", token=owner.csrf(),
                  body={"title": "Assigned Split Fixture", "kind": "manga"})
    (proposed,) = owner.series(root, "?search=Different+Proposed+Group")["items"]
    proposed_url = f"{LIBRARY_ROOTS}/{root}/series/{proposed['key']}"
    assert json.loads(owner.request(proposed_url + "/details", 200))["metadata"] is None
    owner.request(proposed_url + "/metadata", 200, method="PUT", token=owner.csrf(),
                  body={"title": "Reviewed Different Group", "kind": "manga"})
    assert json.loads(owner.request(group_url + "/details", 200))["metadata"]["title"] == "Assigned Split Fixture"
    assert owner.files(root, f"?seriesKey={proposed['key']}&view=library")["total"] == 0

    split_url = group_url + "/split-file"
    file = before[outlier.name]
    change = {"fileId": file["id"], "contentId": file["contentId"]}
    owner.request(split_url, 400, method="POST", body=change)
    owner.request(split_url, 409, method="POST", token=owner.csrf(), body={**change, "contentId": uuid4().hex})
    keeper = before[kept.name]
    owner.request(split_url, 409, method="POST", token=owner.csrf(),
                  body={"fileId": keeper["id"], "contentId": keeper["contentId"]})
    owner.request(split_url, 204, method="POST", token=owner.csrf(), body=change)
    owner.request(split_url, 409, method="POST", token=owner.csrf(), body=change)

    def assert_split():
        (split,) = owner.files(root, "?search=Split+Fixture+Vol.02.pdf")["items"]
        assert split["seriesKey"] != group["key"] and split["id"] == file["id"]
        assert split["contentId"] == file["contentId"] and not split["associationConfirmed"]
        assert owner.files(root, f"?seriesKey={group['key']}&view=library")["total"] >= 1
        assert owner.files(root, f"?seriesKey={split['seriesKey']}&view=library")["total"] == 0
        assert outlier.read_bytes() == b"Split fixture outlier"
        return split["seriesKey"]

    key = assert_split()
    owner.scan(root)
    assert assert_split() == key
    outlier.write_bytes(b"New archive at previously split path")
    owner.scan(root)
    (replacement,) = owner.files(root, "?search=Split+Fixture+Vol.02.pdf")["items"]
    assert replacement["seriesKey"] == group["key"] and replacement["contentId"] != file["contentId"]
    assert not replacement["associationConfirmed"]
    for entry in (kept, outlier, alias):
        entry.unlink()
    owner.scan(root)


def verify_numbered_and_manual_intake_groups(owner, root, path):
    releases = [
        "[Grove (North Smith)] Paper Lantern 1 (x3200) [Owl Press].pdf",
        "[Grove (North Smith)] Paper Lantern 2 (x3200) [Alternate Press].pdf",
        "[North Smith] Paper Lantern 8.pdf",
        "[South Smith] Paper Lantern 3.pdf",
        "Frost Path 1.pdf",
        "Frost Path 2.pdf",
    ]
    for index, name in enumerate(releases):
        (path / name).write_text(f"Disposable release {index}")
    owner.scan(root)
    inferred = {file["relativePath"]: file for file in owner.files(root, "?search=Paper+Lantern")["items"]}
    selected = [inferred[name] for name in releases[:3]]
    key = selected[0]["seriesKey"]
    assert {file["seriesKey"] for file in selected} == {key}
    assert [file["chapter"] for file in selected] == ["1", "2", "8"]
    assert all(file["seriesTitle"] == "Paper Lantern" and not file["associationConfirmed"] for file in selected)
    assert inferred[releases[3]]["seriesKey"] != key and inferred[releases[3]]["chapter"] is None
    assert owner.files(root, "?view=library&search=Paper+Lantern")["total"] == 0

    manual = owner.files(root, "?search=Frost+Path")["items"]
    keys = [file["seriesKey"] for file in manual]
    assert len(set(keys)) == 2
    change = {"seriesKeys": keys, "title": "Frost Path"}
    route = f"{LIBRARY_ROOTS}/{root}/group-series"
    owner.request(route, 400, method="POST", body=change)
    owner.request(route, 409, method="POST", token=owner.csrf(), body={**change, "seriesKeys": [keys[0], sha256(b"stale").hexdigest()]})
    owner.request(route, 204, method="POST", token=owner.csrf(), body=change)
    (group,) = owner.series(root, "?search=Frost+Path")["items"]
    assert group["fileCount"] == 2 and not group["hasMetadata"]
    owner.scan(root)
    files = owner.files(root, "?seriesKey=" + group["key"])["items"]
    assert [file["chapter"] for file in files] == ["1", "2"] and all(not file["associationConfirmed"] for file in files)
    metadata = f"{LIBRARY_ROOTS}/{root}/series/{group['key']}/metadata"
    local_tags = [{"name": "Winter", "spoiler": False}, {"name": "Frozen clue", "spoiler": True}]
    owner.request(metadata, 200, method="PUT", token=owner.csrf(),
                  body={"title": "Frost Path", "kind": "doujin", "tags": local_tags})
    (published,) = owner.series(root, "?view=library&search=Frost+Path")["items"]
    assert published["fileCount"] == 2 and "aniList" not in published

    late = "Frost Path 3.pdf"
    (path / late).write_text("Late unlinked chapter")
    releases.append(late)
    owner.scan(root)
    (late_file,) = owner.files(root, "?search=Frost+Path+3")["items"]
    owner.request(route, 204, method="POST", token=owner.csrf(), body={
        "seriesKeys": [group["key"], late_file["seriesKey"]], "targetSeriesKey": group["key"], "confirm": True,
    })
    appended = owner.files(root, "?view=library&seriesKey=" + group["key"])["items"]
    assert [file["chapter"] for file in appended] == ["1", "2", "3"]
    details = json.loads(owner.request(metadata.rsplit("/", 1)[0] + "/details?published=true", 200))["metadata"]
    assert details["kind"] == "doujin" and details["tags"] == local_tags
    tags = json.loads(owner.request(f"{LIBRARY_ROOTS}/{root}/tags", 200))
    assert "Winter" in tags and "Frozen clue" not in tags
    assert owner.series(root, "?view=library&search=Frost+Path&includeTag=winter")["total"] == 1
    owner.request(route, 409, method="POST", token=owner.csrf(),
                  body={"seriesKeys": [group["key"], key], "title": "New title"})
    for name in releases:
        (path / name).unlink()
    owner.scan(root)


def verify_bulk_release_tags(owner, root, files):
    url = f"/api/versions/{root}"
    selection = {"fileIds": [item["id"] for item in files]}
    owner.request(url + "/batch-preview", 400, method="POST", body=selection)
    owner.request(url + "/batch-preview", 400, method="POST", token=owner.csrf(),
                  body={"fileIds": [files[0]["id"], files[0]["id"].upper()]})
    owner.request(url + "/batch-preview", 409, method="POST", token=owner.csrf(),
                  body={"fileIds": [files[0]["id"], uuid4().hex]})

    def preview():
        return json.loads(owner.request(url + "/batch-preview", 200, method="POST", token=owner.csrf(), body=selection))

    def targets(rows):
        return [{"fileId": row["fileId"], "contentId": row["contentId"], "relativePath": row["relativePath"],
                 "revision": row["tags"]["revision"]} for row in rows]

    original = preview()
    assert [row["fileId"] for row in original] == selection["fileIds"]
    assert original[0]["tags"] == original[1]["tags"] and original[2]["tags"]["revision"] == 0
    batch = {"field": "group", "value": "Batch Fixture", "targets": targets(original)}
    owner.request(url + "/batch", 400, method="PUT", body=batch)
    owner.request(url + "/batch", 400, method="PUT", token=owner.csrf(), body={**batch, "field": "unsupported"})
    moved = [*batch["targets"][:-1], {**batch["targets"][-1], "relativePath": "different.cbz"}]
    owner.request(url + "/batch", 409, method="PUT", token=owner.csrf(), body={**batch, "targets": moved})
    assert preview() == original

    last = original[-1]
    owner.request(url + "/files/" + last["fileId"], 204, method="PUT", token=owner.csrf(),
                  body={**last["tags"], "contentId": last["contentId"], "edition": "Concurrent edition"})
    owner.request(url + "/batch", 409, method="PUT", token=owner.csrf(), body=batch)
    current = preview()
    assert current[:2] == original[:2] and current[-1]["tags"]["edition"] == "Concurrent edition"
    owner.request(url + "/batch", 204, method="PUT", token=owner.csrf(), body={**batch, "targets": targets(current)})
    corrected = preview()
    for before, after in zip(current, corrected):
        assert after["tags"] == {**before["tags"], "group": "Batch Fixture", "revision": before["tags"]["revision"] + 1}
    owner.request(url + "/batch", 204, method="PUT", token=owner.csrf(),
                  body={"field": "edition", "value": None, "targets": targets(corrected[2:])})
    assert preview()[-1]["tags"]["edition"] is None


def verify_file_association(owner, root, path, pending, ordinary, target_key, version):
    endpoint = "/api/file-associations"
    Browser(owner.url).request(endpoint + "/series", 401)
    target_list = json.loads(owner.request(endpoint + "/series?search=opposite&limit=1", 200))
    assert target_list["total"] == 1
    (target,) = target_list["items"]
    assert target["title"] == "Published Parchment" and target["alternateTitle"] == "Opposite Title"
    assert owner.files(root, "?view=unassociated&search=Pending+Signal")["total"] == 2
    published = f"?view=library&seriesKey={target_key}"
    expected = {"contentId": ordinary["contentId"], "seriesKey": ordinary["seriesKey"], "metadataId": target["id"]}
    url = f"{endpoint}/{root}/files/{ordinary['id']}"
    owner.request(url + "/preview", 400, method="POST", body=expected)
    owner.request(url + "/preview", 409, method="POST", token=owner.csrf(), body={**expected, "contentId": uuid4().hex})
    preview = json.loads(owner.request(url + "/preview", 200, method="POST", token=owner.csrf(), body=expected))
    assert preview["count"] == 2 and preview["locations"][0]["relativePath"] == pending and preview["currentTitle"] is None
    owner.request(url, 400, method="PUT", body={**expected, "revision": preview["revision"]})

    data = (path / pending).read_bytes()
    late = path / "Unrelated late alias.cbz"
    late.write_bytes(data)
    owner.scan(root)
    owner.request(url, 409, method="PUT", token=owner.csrf(), body={**expected, "revision": preview["revision"]})
    assert owner.files(root, published)["total"] == 2
    current = json.loads(owner.request(url + "/preview", 200, method="POST", token=owner.csrf(), body=expected))
    assert current["count"] == 3 and current["target"]["id"] == target["id"]

    book = f"/api/reader/{ordinary['contentId']}"
    progress = json.loads(owner.request(book + "/progress", 200, method="PUT", token=owner.csrf(),
                                       body={"updateId": str(uuid4()), "baseRevision": 0, "pageIndex": 0}))
    owner.request(url, 204, method="PUT", token=owner.csrf(), body={**expected, "revision": current["revision"]})
    assert owner.files(root, published)["total"] == 5
    assert owner.files(root, "?view=unassociated&search=Pending+Signal")["total"] == 0
    tags = json.loads(owner.request(version, 200))["tags"]
    assert tags["group"] == "Batch Fixture"
    assert json.loads(owner.request(book, 200))["progress"] == progress
    assert late.read_bytes() == data and (path / pending).read_bytes() == data

    renamed = late.with_name("Relocated archive without its series name.cbz")
    late.rename(renamed)
    owner.scan(root)
    (restored,) = owner.files(root, published + "&search=Relocated+archive")["items"]
    assert restored["contentId"] == ordinary["contentId"] and restored["associationConfirmed"]
    assert json.loads(owner.request(book, 200))["progress"] == progress


def verify_maintenance(owner):
    url = "/api/maintenance"
    roots = owner.roots()
    root, file = next((item, file) for item in roots if item["kind"] in {"library", "both"}
                      for file in owner.files(item["id"], "?limit=100")["items"] if file["hasCover"])
    cover = f"{LIBRARY_ROOTS}/{root['id']}/files/{file['id']}/cover"
    original_cover = owner.request(cover, 200)
    inventory = owner.files(root["id"], "?limit=100")
    owner.request(url + "/artwork-cache/clear", 400, method="POST")
    cleared = json.loads(owner.request(url + "/artwork-cache/clear", 200, method="POST", token=owner.csrf()))
    assert cleared["images"] > 0 and cleared["imageBytes"] > 0
    owner.request(cover, 404)
    after = owner.files(root["id"], "?limit=100")
    assert [(item["id"], item["sha256"]) for item in inventory["items"]] == [(item["id"], item["sha256"]) for item in after["items"]]

    def wait(ids):
        for _ in range(100):
            current = {item["id"]: item["state"] for item in owner.roots() if item["id"] in ids}
            if len(current) == len(ids) and not any(state in {"queued", "scanning"} for state in current.values()):
                return
            time.sleep(0.1)
        raise AssertionError("Maintenance scans did not finish")

    missing = str(uuid4())
    selection = {"rootIds": [root["id"], root["id"], missing]}
    owner.request(url + "/scans", 400, method="POST", body=selection)
    body = owner.request(url + "/scans", 202, method="POST", token=owner.csrf(), body=selection)
    assert json.loads(body) == [{"rootId": root["id"], "state": "queued"}, {"rootId": missing, "state": "missing"}]
    wait({root["id"]})
    assert owner.request(cover, 200) == original_cover
    all_scans = json.loads(owner.request(url + "/scans", 202, method="POST", token=owner.csrf(), body={"allLibraries": True}))
    library_ids = {item["id"] for item in roots if item["kind"] in {"library", "both"}}
    assert {result["rootId"] for result in all_scans} == library_ids
    wait(library_ids)
    reset_preview = json.loads(owner.request(url + "/resets/preview", 200, method="POST", token=owner.csrf(),
                                             body={"level": "progress"}))
    assert any(item["count"] for item in reset_preview["affected"])
    reset = json.loads(owner.request(url + "/resets/apply", 200, method="POST", token=owner.csrf(), body={
        "token": reset_preview["token"], "confirmation": reset_preview["confirmation"],
    }))
    assert reset["level"] == "progress" and reset["backupDirectory"]


def smoke(start, media):
    password = uuid4().hex
    with start(password) as url:
        wait_for_server(url)
        first, second = Browser(url), Browser(url)
        html = first.request("/", 200).decode()
        assert "<title>Manga Organizer</title>" in html
        (script,) = re.findall(r'<script[^>]+src="([^"]+)"', html)
        assert first.request(script, 200)
        (stylesheet,) = re.findall(r'<link[^>]+rel="stylesheet"[^>]+href="([^"]+)"', html)
        assert first.request(stylesheet, 200)
        assert first.headers.get_content_type() == "text/css"
        first.request("/api/server", 401)
        first.request(LIBRARY_ROOTS, 401)
        first.request(f"{LIBRARY_ROOTS}/{uuid4()}/series", 401)
        first.request("/api/library/folders", 401)
        first.request("/api/maintenance/artwork-cache/clear", 401, method="POST")
        first.request("/api/session", 400, method="POST", body={"password": password})
        first.sign_in(uuid4().hex, 401)
        first.sign_in(password)
        (session,) = first.headers.get_all("Set-Cookie")
        assert session.startswith("MangaOrganizer.Session.") and "; httponly" in session.lower()
        server_id = first.server_id()
        second.request("/api/server", 401)
        second.sign_in(password)
        assert second.server_id() == server_id
        scan_library(first, media)
        verify_file_identity(first, media)
        roots = verify_folder_settings(first, media)
        verify_reader(first, second, roots[-1]["id"], media / "intake")
        verify_volume_chapters(first, roots[-1]["id"], media / "intake")
        verify_catalog_visibility(first, roots[-1]["id"], media / "intake")
        verify_intake_file_split(first, roots[-1]["id"], media / "intake")
        verify_numbered_and_manual_intake_groups(first, roots[-1]["id"], media / "intake")
        verify_maintenance(first)
        roots = first.roots()
        inventory = first.files(roots[0]["id"], "?limit=100")
        series = first.series(roots[-1]["id"])
        assert second.roots() == roots
        first.request("/api/session", 400, method="DELETE")
        first.request("/api/session", 204, method="DELETE", token=first.csrf())
        first.request("/api/server", 401)
        assert second.server_id() == server_id

    with start(None) as url:
        wait_for_server(url)
        browser = Browser(url, second.cookies)
        assert browser.server_id() == server_id
        browser.sign_in(password)
        assert browser.server_id() == server_id
        assert browser.roots() == roots
        assert browser.files(roots[0]["id"], "?limit=100") == inventory
        assert browser.series(roots[-1]["id"]) == series


def create_media_fixture(media):
    primary, archive = media / "primary", media / "archive"
    (primary / "nested").mkdir(parents=True)
    archive.mkdir()
    media.chmod(0o755)
    (primary / "nested" / "Chapter 12.5.CBZ").write_bytes(b"Synthetic chapter inventory fixture" * 5000)
    for index in range(52):
        (primary / f"Issue {index:02}.pdf").write_bytes(b"Synthetic inventory fixture")
    (primary / "notes.txt").write_text("Ignored by the inventory.")
    (archive / "Volume.epub").write_bytes(b"Synthetic volume inventory fixture")
    for folder in ("extra", "spare", "intake"):
        (media / folder).mkdir()
    (media / "extra" / "Keep.cbz").write_bytes(b"Synthetic file outside the catalog after a settings update")
    (media / "intake" / "Unmatched.cbz").write_bytes(b"Synthetic unmatched intake file")
    if os.name == "nt":
        subprocess.run(["cmd", "/c", "mklink", "/J", str(primary / "linked-folder"), str(archive)],
                       check=True, stdout=subprocess.DEVNULL)
    else:
        (primary / "linked-folder").symlink_to("../archive", target_is_directory=True)
        (primary / "linked-file.epub").symlink_to("../archive/Volume.epub")


def create_managed_fixture(media):
    for path in (media, media / "primary", media / "archive"):
        path.mkdir()
        path.chmod(0o777)
    with ZipFile(media / "primary" / "Chapter 1.cbz", "w", compression=ZIP_DEFLATED) as archive:
        archive.writestr("pages/1.png", synthetic_png(32, 160, 122))


def verify_routed_intake(owner, media, source, target):
    source_id, target_id = source["id"], target["id"]
    route = f"{LIBRARY_ROOTS}/{source_id}/intake-target"
    operations = "/api/file-operations"
    ready = media / "primary" / "Routed Release Vol.01.pdf"
    pending = media / "primary" / "Waiting Release Vol.01.pdf"
    destination = media / "archive" / ready.name
    unnumbered = media / "primary" / "Unnumbered Fixture.cbz"
    with ZipFile(unnumbered, "w") as archive:
        archive.writestr("01.png", synthetic_png(90, 110, 140))
    ready.write_bytes(b"Confirmed routed fixture")
    pending.write_bytes(b"Unconfirmed routed fixture")
    owner.scan(source_id)
    indexed = {file["relativePath"]: file for file in owner.files(source_id)["items"]}
    ready_plan = {
        "sourceRoot": source_id, "targetRoot": target_id, "intakeTransfer": True,
        "fileId": indexed[ready.name]["id"], "targetPath": ready.name,
    }
    pending_plan = {**ready_plan, "fileId": indexed[pending.name]["id"], "targetPath": pending.name}
    owner.request(route, 400, method="PUT", body={"targetRoot": target_id})
    owner.request(route, 400, method="PUT", token=owner.csrf(), body={"targetRoot": str(uuid4())})

    def set_target(target_root):
        owner.request(route, 204, method="PUT", token=owner.csrf(), body={"targetRoot": target_root})

    def preview(body, expected=200):
        return json.loads(owner.request(f"{operations}/preview", expected, method="POST", token=owner.csrf(), body=body))

    key = indexed[ready.name]["seriesKey"]
    owner.request(f"{LIBRARY_ROOTS}/{source_id}/series/{key}/metadata", 200, method="PUT", token=owner.csrf(),
                  body={"title": "Routed Release", "kind": "manga"})
    preview(ready_plan, 409)
    set_target(target_id)
    preview(pending_plan, 409)
    ready_item = owner.files(source_id, "?search=Routed+Release")["items"][0]
    assert ready_item["intakePath"] == "Routed Release/Routed Release Vol.1.pdf"
    no_number = indexed[unnumbered.name]
    volume_url = f"{LIBRARY_ROOTS}/{source_id}/series/{no_number['seriesKey']}"
    owner.request(volume_url + "/metadata", 200, method="PUT", token=owner.csrf(),
                  body={"title": "Unnumbered Fixture", "kind": "manga"})
    draft = {**ready_plan, "fileId": no_number["id"], "targetPath": unnumbered.name}
    preview(draft, 409)
    whole_volume = {"contentId": no_number["contentId"], "volume": "07", "chapters": [], "wholeVolume": True}
    owner.request(volume_url + "/coverage", 204, method="PUT", token=owner.csrf(), body=whole_volume)
    owner.scan(source_id)
    (volume_item,) = owner.files(source_id, f"?seriesKey={no_number['seriesKey']}&volume=7")["items"]
    assert volume_item["volume"] == "7" and volume_item["chapter"] is None
    assert volume_item["intakePath"] == "Unnumbered Fixture/Unnumbered Fixture Vol.7.cbz"
    details = json.loads(owner.request(volume_url + "/details?published=true", 200))
    assert details["volumes"] == details["readableVolumes"] == ["7"] and details["availableChapters"] == 0
    (release,) = json.loads(owner.request(volume_url + "/volume-releases?volume=7&published=true", 200))
    assert release["contentId"] == no_number["contentId"] and release["unresolvedVolume"]
    owner.request(volume_url + "/coverage", 204, method="PUT", token=owner.csrf(), body={**whole_volume, "volume": "", "wholeVolume": False})
    preview(draft, 409)
    owner.request(volume_url + "/coverage", 204, method="PUT", token=owner.csrf(), body=whole_volume)
    volume_target = media / "archive" / volume_item["intakePath"]
    # Match the other managed fixture folders shared by the host and the non-root Docker user.
    volume_target.parent.mkdir()
    volume_target.parent.chmod(0o777)
    volume_plan = preview({**draft, "targetPath": volume_item["intakePath"]})
    owner.request(f"{operations}/{volume_plan['id']}/apply", 200, method="POST", token=owner.csrf())
    assert not unnumbered.exists() and volume_target.is_file()
    (transferred,) = owner.files(target_id, f"?seriesKey={no_number['seriesKey']}&volume=7&view=library")["items"]
    assert transferred["contentId"] == volume_item["contentId"]
    owner.request(f"{operations}/{volume_plan['id']}/restore", 200, method="POST", token=owner.csrf())
    assert unnumbered.is_file() and not volume_target.exists()
    unnumbered.unlink()
    mixed = json.loads(owner.request(f"{operations}/batch-preview", 200, method="POST", token=owner.csrf(),
                                     body={"items": [ready_plan, pending_plan]}))
    assert mixed[0]["plan"]["target"].endswith(ready.name) and mixed[1]["plan"] is None
    assert ready.exists() and pending.exists() and not destination.exists()
    stale_id = mixed[0]["plan"]["id"]

    set_target(source_id)
    preview({**ready_plan, "targetRoot": source_id, "targetPath": "No transfer.pdf"}, 409)
    owner.request(f"{operations}/{stale_id}/apply", 409, method="POST", token=owner.csrf())
    assert ready.exists() and not destination.exists()
    assert owner.files(source_id, "?view=library&search=Routed+Release")["total"] == 1
    set_target(target_id)

    offline = media / "temporarily-offline"
    destination.parent.rename(offline)
    try:
        owner.scan(target_id, "unavailable")
        preview(ready_plan, 409)
    finally:
        offline.rename(destination.parent)
    owner.scan(target_id)

    destination.write_bytes(b"Pre-existing protected destination")
    preview(ready_plan, 409)
    destination.unlink()
    accepted = preview(ready_plan)
    operation = f"{operations}/{accepted['id']}"
    applied = json.loads(owner.request(f"{operation}/apply", 200, method="POST", token=owner.csrf()))
    assert applied["state"] == "completed" and applied["recovery"]
    assert not ready.exists() and destination.read_bytes() == b"Confirmed routed fixture" and pending.exists()
    assert owner.files(target_id, "?view=library&search=Routed+Release")["total"] == 1
    set_target(source_id)
    owner.request(f"{operation}/restore", 200, method="POST", token=owner.csrf())
    assert ready.read_bytes() == b"Confirmed routed fixture" and not destination.exists()

    set_target(target_id)
    owner.request(f"{LIBRARY_ROOTS}/{target_id}", 204, method="DELETE", token=owner.csrf())
    assert next(folder for folder in owner.roots() if folder["id"] == source_id)["intakeTargetId"] is None
    preview(ready_plan, 409)
    target_settings = {"path": target["path"], "name": target["name"], "kind": "both", "managed": True}
    readded = json.loads(owner.request(LIBRARY_ROOTS, 200, method="POST", token=owner.csrf(), body=target_settings))
    assert readded["id"] == target_id
    set_target(target_id)
    ready.unlink()
    pending.unlink()
    owner.scan(source_id)


def verify_managed_files(start, media, *, shared_permissions=False, data_directory=None):
    password = uuid4().hex
    original = media / "primary" / "Chapter 1.cbz"
    original_bytes = original.read_bytes()
    target = media / "archive" / "Sorted" / "Moved.cbz"
    if shared_permissions:
        target.parent.mkdir()
        target.parent.chmod(0o777)
    operations = "/api/file-operations"
    with start(password) as url:
        wait_for_server(url)
        owner = Browser(url)
        owner.request(operations, 401)
        owner.sign_in(password)
        primary, archive = owner.roots()
        owner.scan(primary["id"])
        (item,) = owner.files(primary["id"])["items"]
        (series,) = owner.series(primary["id"])["items"]
        original_cover = f"{LIBRARY_ROOTS}/{primary['id']}/series/{series['key']}/cover"
        moved_cover = f"{LIBRARY_ROOTS}/{archive['id']}/series/{series['key']}/cover"
        cover_bytes = owner.request(original_cover, 200)
        plan = {"sourceRoot": primary["id"], "fileId": item["id"], "targetRoot": archive["id"], "targetPath": "Sorted/Moved.cbz"}
        owner.request(f"{operations}/preview", 409, method="POST", token=owner.csrf(), body=plan)
        for root in (primary, archive):
            options = {"path": root["path"], "kind": "both", "name": root["name"], "managed": True}
            saved = json.loads(owner.request(f"{LIBRARY_ROOTS}/{root['id']}", 200, method="PUT", token=owner.csrf(), body=options))
            assert saved["managed"] and saved["id"] == root["id"]

        verify_routed_intake(owner, media, primary, archive)

        def preview(path, destination=archive["id"], expected=200, **options):
            return json.loads(owner.request(f"{operations}/preview", expected, method="POST", token=owner.csrf(),
                                            body=dict(plan, targetPath=path, targetRoot=destination, **options)))

        preview("../Escaped.cbz", expected=409)
        collision = preview("Occupied.cbz")
        occupied = media / "archive" / "Occupied.cbz"
        occupied.write_bytes(b"Existing copy protected")
        owner.request(f"{operations}/{collision['id']}/apply", 409, method="POST", token=owner.csrf())
        assert occupied.read_bytes() == b"Existing copy protected" and original.read_bytes() == original_bytes

        move = preview("Sorted/Moved.cbz")
        assert move["state"] == "planned" and not target.exists() and target.parent.exists() == shared_permissions
        operation = f"{operations}/{move['id']}"
        owner.request(f"{operation}/apply", 400, method="POST")
        completed = json.loads(owner.request(f"{operation}/apply", 200, method="POST", token=owner.csrf()))
        assert completed["state"] == "completed" and completed["recovery"]
        assert not original.exists() and target.read_bytes() == original_bytes
        assert owner.files(primary["id"])["total"] == 0
        (moved,) = owner.files(archive["id"])["items"]
        assert moved["id"] == item["id"] and moved["contentId"] == item["contentId"]
        assert owner.request(moved_cover, 200) == cover_bytes
        assert json.loads(owner.request(f"{operation}/apply", 200, method="POST", token=owner.csrf())) == completed

    with start(None) as url:
        wait_for_server(url)
        owner = Browser(url)
        owner.sign_in(password)
        assert next(root for root in owner.roots() if root["id"] == primary["id"])["intakeTargetId"] == archive["id"]
        history = json.loads(owner.request(operations, 200))
        assert any(entry["id"] == move["id"] and entry["state"] == "completed" for entry in history)
        restored = json.loads(owner.request(f"{operation}/restore", 200, method="POST", token=owner.csrf()))
        assert restored["state"] == "restored" and not target.exists() and original.read_bytes() == original_bytes
        (restored_file,) = owner.files(primary["id"])["items"]
        assert restored_file["id"] == item["id"] and restored_file["contentId"] == item["contentId"]
        assert owner.request(original_cover, 200) == cover_bytes

        case_change = preview("chapter 1.cbz", destination=primary["id"])
        case_url = f"{operations}/{case_change['id']}"
        owner.request(f"{case_url}/apply", 200, method="POST", token=owner.csrf())
        assert "chapter 1.cbz" in {file.name for file in original.parent.iterdir()}
        owner.request(f"{case_url}/restore", 200, method="POST", token=owner.csrf())
        assert original.name in {file.name for file in original.parent.iterdir()}

        variants = media / "archive" / "primary"
        variants.mkdir()
        variants.chmod(0o777)
        later_chapter = variants / "Chapter 10.cbz"
        with ZipFile(later_chapter, "w", compression=ZIP_DEFLATED) as later:
            later.writestr("pages/1.png", synthetic_png(212, 123, 197, 15, 26))
        owner.scan(archive["id"])
        assert webp_dimensions(owner.request(moved_cover, 200)) == (15, 26)
        earliest = preview("primary/Chapter 1.cbz")
        earliest_operation = f"{operations}/{earliest['id']}"
        owner.request(f"{earliest_operation}/apply", 200, method="POST", token=owner.csrf())
        assert owner.request(moved_cover, 200) == cover_bytes
        owner.request(f"{earliest_operation}/restore", 200, method="POST", token=owner.csrf())
        assert webp_dimensions(owner.request(moved_cover, 200)) == (15, 26)
        later_chapter.unlink()
        owner.scan(archive["id"])

        # Distinct same-chapter editions and their folder images can share a final directory.
        existing_release = variants / "Chapter 1.cbz"
        with ZipFile(existing_release, "w", compression=ZIP_DEFLATED) as translated:
            translated.writestr("pages/1.png", synthetic_png(218, 102, 65))
        existing_bytes = existing_release.read_bytes()
        existing_cover = synthetic_png(61, 110, 184, 15, 21)
        (variants / "cover.png").write_bytes(existing_cover)
        source_covers = [synthetic_png(140, 91, 72, 18, 23), synthetic_png(85, 190, 126, 23, 34)]
        for name, cover in zip(("cover.png", "cover-1.png"), source_covers):
            (original.parent / name).write_bytes(cover)

        variant_path = "primary/Chapter 1.cbz"
        preview(variant_path, expected=409)
        plan_with_covers = preview(variant_path, keepBoth=True, copyFolderCovers=True)
        assert Path(plan_with_covers["target"]).name == "Chapter 1 [Variant 1].cbz"
        assert [Path(cover["target"]).name for cover in plan_with_covers["covers"]] == ["cover-1.png", "cover-2.png"]
        assert not any(cover["published"] for cover in plan_with_covers["covers"])
        blocked_cover = variants / "cover-1.png"
        blocked_cover.write_bytes(b"An independent file created after preview")
        owner.request(f"{operations}/{plan_with_covers['id']}/apply", 409, method="POST", token=owner.csrf())
        assert original.read_bytes() == original_bytes and blocked_cover.read_bytes() == b"An independent file created after preview"
        blocked_cover.unlink()

        plan_with_covers = preview(variant_path, keepBoth=True, copyFolderCovers=True)
        variant_operation = f"{operations}/{plan_with_covers['id']}"
        result = json.loads(owner.request(f"{variant_operation}/apply", 200, method="POST", token=owner.csrf()))
        assert result["state"] == "completed" and all(cover["published"] for cover in result["covers"])
        assert existing_release.read_bytes() == existing_bytes
        assert (variants / "Chapter 1 [Variant 1].cbz").read_bytes() == original_bytes
        assert (variants / "cover.png").read_bytes() == existing_cover
        assert [(variants / f"cover-{index}.png").read_bytes() for index in (1, 2)] == source_covers
        assert [(original.parent / name).read_bytes() for name in ("cover.png", "cover-1.png")] == source_covers
        owner.scan(archive["id"])
        (variant_group,) = [group for group in owner.series(archive["id"])["items"] if group["key"] == series["key"]]
        assert variant_group["fileCount"] == 2
        assert webp_dimensions(owner.request(moved_cover, 200)) == (15, 21)
        restored_variant = json.loads(owner.request(f"{variant_operation}/restore", 200, method="POST", token=owner.csrf()))
        assert restored_variant["state"] == "restored" and original.read_bytes() == original_bytes
        assert [(variants / f"cover-{index}.png").read_bytes() for index in (1, 2)] == source_covers

        second_source = original.parent / "Batch Chapter 2.cbz"
        second_source.write_bytes(b"Another indexed archive fixture")
        owner.scan(primary["id"])
        (second_file,) = [file for file in owner.files(primary["id"])["items"] if file["relativePath"] == second_source.name]
        second_plan = dict(plan, fileId=second_file["id"])
        collision_plans = {"items": [dict(plan, targetPath="Batch.cbz"), dict(second_plan, targetPath="Batch.cbz")]}
        owner.request(operations + "/batch-preview", 400, method="POST", body=collision_plans)
        collisions = json.loads(owner.request(operations + "/batch-preview", 200, method="POST", token=owner.csrf(), body=collision_plans))
        assert all(item["plan"] is None and "conflicts" in item["error"] for item in collisions)
        planned = json.loads(owner.request(operations + "/batch-preview", 200, method="POST", token=owner.csrf(), body={"items": [
            dict(plan, targetPath="Batch-one.cbz"), dict(second_plan, targetPath="Batch-two.cbz")]}))
        assert all(item["plan"] and not item["error"] for item in planned)
        applied = json.loads(owner.request(operations + "/batch-apply", 200, method="POST", token=owner.csrf(), body={"ids": [planned[0]["plan"]["id"]]}))
        assert len(applied) == 1 and applied[0]["result"]["state"] == "completed"
        assert second_source.exists() and not original.exists()
        owner.request(f"{operations}/{planned[0]['plan']['id']}/restore", 200, method="POST", token=owner.csrf())
        assert original.read_bytes() == original_bytes
        second_source.unlink()
        owner.scan(primary["id"])

        review_duplicates(owner, primary["id"], original.parent, shared_permissions=shared_permissions)
        verify_folder_duplicate_removal(owner, media, primary["id"], archive["id"])

        changed = preview("Changed.cbz")
        old = original.stat()
        modified = b"!" + original_bytes[1:]
        replacement = original.with_suffix(".replacement")
        replacement.write_bytes(modified)
        os.utime(replacement, ns=(old.st_atime_ns, old.st_mtime_ns))
        replacement.replace(original)
        owner.request(f"{operations}/{changed['id']}/apply", 409, method="POST", token=owner.csrf())
        assert original.read_bytes() == modified and not (media / "archive" / "Changed.cbz").exists()
        review = next(operation for operation in json.loads(owner.request(operations, 200)) if operation["id"] == changed["id"])
        assert review["state"] == "review" and review["recovery"] is None

        if data_directory is None:
            return

        original.write_bytes(original_bytes)
        interrupted = preview("Interrupted.cbz")

    # Mimic an interruption after verification and parking, before the catalog commit.
    interrupted_id = UUID(interrupted["id"]).hex
    backup = data_directory / "file-recovery" / f"{interrupted_id}.backup"
    backup.write_bytes(original_bytes)
    original.rename(original.with_name(f".manga-organizer-{interrupted_id}-original.recovery"))
    blocked_target = media / "archive" / "Interrupted.cbz"
    blocked_target.write_bytes(b"A file created independently before recovery")
    with closing(sqlite3.connect(data_directory / "organizer.sqlite")) as database, database:
        changed_rows = database.execute("UPDATE file_operation SET state = 'applying' WHERE lower(id) = ?", (interrupted["id"],))
        assert changed_rows.rowcount == 1

    with start(None) as url:
        wait_for_server(url)
        owner = Browser(url)
        owner.sign_in(password)
        history = json.loads(owner.request(operations, 200))
        assert any(entry["id"] == interrupted["id"] and entry["state"] == "review" for entry in history)
        result = json.loads(owner.request(f"{operations}/{interrupted['id']}/restore", 200, method="POST", token=owner.csrf()))
        assert result["state"] == "restored" and original.read_bytes() == original_bytes
        assert blocked_target.read_bytes() == b"A file created independently before recovery"


def review_duplicates(owner, root, directory, *, shared_permissions=False):
    if shared_permissions:
        quarantine_directory = directory / ".manga-organizer-quarantine"
        quarantine_directory.mkdir()
        quarantine_directory.chmod(0o777)
    volume = directory / "Safety Fixture Vol.01 Ch.1-3.cbz"
    chapter = directory / "Safety Fixture Vol.01 Ch.1.cbz"
    unrelated_chapter = directory / "Safety Fixture Vol.01 Ch.9.cbz"
    copy = directory / "Safety Fixture Vol.01 Ch.1-3 [Variant 1].cbz"
    alias = directory / "Safety Fixture Vol.01 Ch.1 [Variant 1].cbz"
    for index, path in enumerate((volume, chapter, unrelated_chapter)):
        with ZipFile(path, "w", compression=ZIP_DEFLATED) as archive:
            archive.writestr("1.png", synthetic_png(125, index + 5, 35))
    copy.write_bytes(volume.read_bytes())
    os.link(chapter, alias)
    owner.scan(root)
    files = {item["relativePath"]: item for item in owner.files(root)["items"]}
    operations = "/api/file-operations"

    def compare(path):
        return json.loads(owner.request(f"{operations}/duplicates/{root}/{files[path.name]['id']}", 200))

    def quarantine(source, retain, status):
        return json.loads(owner.request(operations + "/quarantine-preview", status, method="POST", token=owner.csrf(), body={
            "rootId": root, "sourceFile": files[source.name]["id"], "keepFile": files[retain.name]["id"],
        }))

    review = compare(volume)
    assert review["source"]["volumeArchive"]
    comparisons = {item["candidate"]["relativePath"]: item for item in review["comparisons"]}
    (series,) = owner.series(root, "?search=Safety+Fixture")["items"]
    versions = "/api/versions"
    assert json.loads(owner.request(versions + "/series?search=Safety+Fixture", 200))["total"] == 0
    index = f"{operations}/duplicates/series"
    listed = json.loads(owner.request(f"{index}?search=Safety+Fixture&kind=bytes&limit=1", 200))
    assert listed == {"total": 1, "items": [{
        "rootId": root, "seriesKey": series["key"], "title": "Safety Fixture", "candidateFiles": 4,
    }]}
    assert json.loads(owner.request(f"{index}?search=Safety+Fixture&kind=volume", 200)) == {"total": 0, "items": []}
    owner.request(f"{index}?kind=unknown", 400)
    queue = f"{operations}/duplicates/{root}/series/{series['key']}/queue"
    first = json.loads(owner.request(f"{queue}?limit=2", 200))
    second = json.loads(owner.request(f"{queue}?limit=2&offset=2", 200))
    assert first["total"] == second["total"] == 4
    assert {item["relativePath"] for item in first["items"] + second["items"]} == {volume.name, chapter.name, copy.name, alias.name}
    verify_duplicate_decisions(owner, root, series["key"], files[chapter.name])
    owner.request(f"{LIBRARY_ROOTS}/{root}/series/{series['key']}/edition-volumes", 204, method="PUT", token=owner.csrf(),
                  body={"edition": "Safety fixture", "volume": "1", "chapters": ["1", "24"], "contentIds": []})
    series_review = json.loads(owner.request(f"{operations}/duplicates/{root}/series/{series['key']}", 200))
    assert series_review["canReviewQuarantine"]
    assert {item["relativePath"] for item in series_review["files"]} == {volume.name, chapter.name, copy.name, alias.name, unrelated_chapter.name}
    assert {item["chapter"] for item in series_review["expected"]} == {"1", "24"}
    assert unrelated_chapter.name not in comparisons
    overlap = comparisons[chapter.name]
    assert overlap["kind"] == "overlap" and not overlap["canQuarantine"]
    assert not overlap["candidate"]["volumeArchive"]
    assert [item["chapter"] for item in overlap["shared"]] == ["1"]
    assert [item["chapter"] for item in overlap["sourceOnly"]] == ["2", "3"]
    assert comparisons[copy.name]["canQuarantine"]
    assert any(item["kind"] == "hardlink" and not item["canQuarantine"] for item in compare(chapter)["comparisons"])
    quarantine(volume, chapter, 409)
    quarantine(chapter, volume, 409)
    quarantine(chapter, alias, 409)
    preview = quarantine(copy, volume, 200)
    backup_source = volume.read_bytes()
    before = volume.stat()
    volume.write_bytes(b"!" + backup_source[1:])
    os.utime(volume, ns=(before.st_atime_ns, before.st_mtime_ns))
    owner.request(f"{operations}/{preview['id']}/apply", 409, method="POST", token=owner.csrf())
    assert copy.read_bytes() == backup_source
    volume.write_bytes(backup_source)
    owner.scan(root)
    confirmed = quarantine(copy, volume, 200)
    url = f"{operations}/{confirmed['id']}"
    (batch_item,) = json.loads(owner.request(operations + "/batch-apply", 200, method="POST", token=owner.csrf(), body={"ids": [confirmed["id"]]}))
    result = batch_item["result"]
    assert result["kind"] == "quarantine" and result["state"] == "completed"
    assert not copy.exists() and volume.read_bytes() == backup_source
    assert Path(result["target"]).name.endswith(".recovery")
    assert copy.name not in {item["relativePath"] for item in owner.files(root)["items"]}
    owner.scan(root)
    assert owner.files(root, "?search=recovery")["total"] == 0
    owner.request(url + "/restore", 200, method="POST", token=owner.csrf())
    assert copy.read_bytes() == volume.read_bytes() == backup_source

    unassigned = directory / "Safety Fixture Vol.02.cbz"
    alternate = directory / "Safety Fixture Vol.02 [Alternate].zip"
    translated = directory / "Safety Fixture Vol.01 Ch.1 [Translated].cbz"
    for path, count in ((unassigned, 2), (alternate, 3)):
        with ZipFile(path, "w", compression=ZIP_DEFLATED) as archive:
            for page in range(count):
                archive.writestr(f"{page + 1}.png", synthetic_png(125, 25, page + 5))
            archive.writestr("ComicInfo.xml", "<ComicInfo><Series>Safety Fixture</Series><Volume>2</Volume>"
                             "<Publisher>Fixture press</Publisher><PageCount>50</PageCount></ComicInfo>")
    with ZipFile(translated, "w") as archive:
        archive.writestr("01.png", synthetic_png(192, 140, 122))
    owner.scan(root)
    files = {item["relativePath"]: item for item in owner.files(root)["items"]}
    unresolved = compare(unassigned)
    assert unresolved["source"]["chapters"] == []
    (candidate,) = unresolved["comparisons"]
    assert candidate["kind"] == "same-volume" and not candidate["canQuarantine"]
    assert candidate["candidate"]["relativePath"] == alternate.name and candidate["candidate"]["format"] == "zip"
    narrowed = json.loads(owner.request(f"{queue}?kind=volume", 200))
    assert narrowed["total"] == 2
    assert {item["relativePath"] for item in narrowed["items"]} == {unassigned.name, alternate.name}
    evidence = json.loads(owner.request(f"{operations}/duplicates/{root}/{files[unassigned.name]['id']}/archive", 200))
    assert evidence["imagePages"] == 2
    assert {field["label"]: field["value"] for field in evidence["comicInfo"]} == {
        "Series": "Safety Fixture", "Volume": "2", "Publisher": "Fixture press", "Declared page count": "50",
    }
    quarantine(unassigned, alternate, 409)

    page = json.loads(owner.request(versions + "/series?search=Safety+Fixture&limit=1", 200))
    assert page["total"] == 1 and page["items"][0]["releaseCount"] == 4
    version_series = f"{versions}/{root}/series/{series['key']}"
    rows = json.loads(owner.request(version_series, 200))["items"]
    assert {file["reason"] for file in rows} == {"span", "volume"}
    assert len({file["contentId"] for file in rows}) == 4
    release = next(file for file in rows if file["relativePath"] == alternate.name)
    release_url = f"{versions}/{root}/files/{release['fileId']}"
    tags = {"contentId": release["contentId"], "revision": 0, "language": "en-US", "color": "full-color",
            "censorship": "mixed", "edition": "Digital edition", "group": "Fixture group"}
    owner.request(release_url, 400, method="PUT", token=owner.csrf(), body={**tags, "language": "en @invalid"})
    owner.request(release_url, 409, method="PUT", token=owner.csrf(), body={**tags, "contentId": uuid4().hex})
    owner.request(release_url, 204, method="PUT", token=owner.csrf(), body=tags)
    owner.request(release_url, 409, method="PUT", token=owner.csrf(), body=tags)
    owner.scan(root)
    filtered = json.loads(owner.request(version_series + "?language=en-US&color=full-color&censorship=mixed&edition=digital"
                                       "&group=fixture&reviewed=true&limit=1", 200))
    assert filtered["total"] == 1 and filtered["items"][0]["tags"] == {
        "language": "en-us", "color": "full-color", "censorship": "mixed", "edition": "Digital edition",
        "group": "Fixture group", "revision": 1,
    }
    assert json.loads(owner.request(f"{operations}/duplicates/series?search=Safety+Fixture&kind=bytes", 200))["total"] == 1

    for path in (volume, chapter, copy, alias, unrelated_chapter, unassigned, alternate, translated):
        path.unlink()
    owner.scan(root)


def verify_folder_duplicate_removal(owner, media, source_root, retained_root):
    source = media / "primary" / "Folder Fixture"
    retained = media / "archive" / "Folder Fixture"
    source.mkdir()
    retained.mkdir()
    source.chmod(0o777)
    retained.chmod(0o777)
    filename = "Folder Fixture Ch.01.cbz"
    another = source / "Folder Fixture Ch.02.cbz"
    for index, path in enumerate((source / filename, another)):
        with ZipFile(path, "w") as archive:
            archive.writestr("01.png", synthetic_png(120, 31 + index, 80))
    contents = (source / filename).read_bytes()
    (retained / filename).write_bytes(contents)
    owner.scan(source_root)
    owner.scan(retained_root)
    (series,) = owner.series(source_root, "?search=Folder+Fixture")["items"]
    operations = "/api/file-operations"
    url = f"{operations}/duplicates/{source_root}/series/{series['key']}"
    local = json.loads(owner.request(url, 200))["files"]
    assert {file["rootId"] for file in local} == {source_root}
    files = json.loads(owner.request(url + "/folder-files", 200))
    assert len(files) == 3 and {file["rootId"] for file in files} == {source_root, retained_root}
    left = next(file for file in files if file["rootId"] == source_root and file["relativePath"].endswith(filename))
    right = next(file for file in files if file["rootId"] == retained_root)
    distinct = next(file for file in local if file["fileId"] != left["fileId"])
    body = {"rootId": source_root, "sourceFile": left["fileId"], "keepRootId": retained_root, "keepFile": right["fileId"]}
    endpoint = operations + "/quarantine-preview"
    owner.request(endpoint, 409, method="POST", token=owner.csrf(), body={**body, "sourceFile": distinct["fileId"]})
    first = json.loads(owner.request(endpoint, 200, method="POST", token=owner.csrf(), body=body))
    moved = retained / "Missing.cbz"
    (retained / filename).rename(moved)
    owner.request(f"{operations}/{first['id']}/apply", 409, method="POST", token=owner.csrf())
    assert (source / filename).read_bytes() == contents
    moved.rename(retained / filename)
    second = json.loads(owner.request(endpoint, 200, method="POST", token=owner.csrf(), body=body))
    operation = f"{operations}/{second['id']}"
    result = json.loads(owner.request(operation + "/apply", 200, method="POST", token=owner.csrf()))
    assert result["state"] == "completed" and not (source / filename).exists() and another.exists()
    assert (retained / filename).read_bytes() == contents
    (retained / filename).unlink()
    result = json.loads(owner.request(operation + "/restore", 200, method="POST", token=owner.csrf()))
    assert result["state"] == "restored" and (source / filename).read_bytes() == contents
    (source / filename).unlink()
    another.unlink()
    retained.rmdir()
    owner.scan(source_root)


def verify_duplicate_decisions(owner, root, series, chapter):
    api = f"/api/file-operations/duplicates/{root}/series/{series}"
    url = api + "/decisions"
    index = "/api/file-operations/duplicates/series?search=Safety+Fixture"
    Browser(owner.url).request(url, 401)

    def summary():
        return json.loads(owner.request(url, 200))

    def save(kind, finding, decision, status=204, note=None):
        owner.request(f"{url}/{kind}", status, method="PUT", token=owner.csrf(), body={
            "fingerprint": finding["fingerprint"], "latest": finding["latest"], "decision": decision, "note": note,
        })

    original = {item["kind"]: item for item in summary()["groups"]}
    assert set(original) == {"bytes", "chapter"} and all(item["status"] == "pending" for item in original.values())
    body = {"fingerprint": original["bytes"]["fingerprint"], "latest": 0, "decision": "defer"}
    owner.request(url + "/bytes", 400, method="PUT", body=body)
    save("chapter", original["chapter"], "keep", note="Keep the standalone and volume.")
    save("chapter", original["chapter"], "keep", status=409)
    save("bytes", original["bytes"], "defer")
    assert json.loads(owner.request(index + "&status=pending", 200))["total"] == 0
    assert json.loads(owner.request(index + "&status=kept&kind=chapter", 200))["total"] == 1
    assert json.loads(owner.request(index + "&status=deferred&kind=bytes", 200))["total"] == 1
    page = json.loads(owner.request(api + "/queue?status=kept&limit=1", 200))
    assert page["total"] == 4 and page["items"][0]["reasons"] == ["chapter"]
    owner.request(index + "&status=other", 400)

    before = summary()
    owner.scan(root)
    assert summary() == before
    version = f"/api/versions/{root}/files/{chapter['id']}"
    owner.request(version, 204, method="PUT", token=owner.csrf(),
                  body={"contentId": chapter["contentId"], "revision": 0, "language": "en"})
    changed = summary()
    by_kind = {item["kind"]: item for item in changed["groups"]}
    assert by_kind["chapter"]["status"] == "pending" and by_kind["chapter"]["evidenceChanged"]
    assert by_kind["bytes"]["status"] == "deferred" and not by_kind["bytes"]["evidenceChanged"]
    assert changed["recent"] == before["recent"]
    assert changed["recent"][1]["note"] == "Keep the standalone and volume."
    save("bytes", by_kind["bytes"], "reopen")
    assert summary()["recent"][0]["decision"] == "reopen"
    assert json.loads(owner.request(index + "&status=pending&kind=bytes", 200))["total"] == 1


def verify_metadata_queue(executable, fixture):
    media = fixture / "queue-media"
    (media / "primary").mkdir(parents=True)
    (media / "archive").mkdir()
    for index, name in enumerate(("Alpha Shelf Vol.01", "Alpha Shelf Vol.02", "Beta Shelf Vol.01", "Gamma Shelf Vol.01")):
        (media / "primary" / f"{name}.pdf").write_bytes(f"Queue fixture {index}".encode())
    calls = []
    page_started = threading.Event()
    release_page = threading.Event()

    class Provider(BaseHTTPRequestHandler):
        def do_POST(self):
            variables = json.loads(self.rfile.read(int(self.headers["Content-Length"])))["variables"]
            title = variables.get("title")
            calls.append((title, time.monotonic()))
            if title and title.startswith("Page Window") and not page_started.is_set():
                page_started.set()
                release_page.wait(timeout=8)
            if title == "Rate Shelf" and sum(name == title for name, _ in calls) == 1:
                self.send_response(429)
                self.send_header("Retry-After", "4")
                self.end_headers()
                return
            matches = {
                "Alpha Shelf": [{"id": 21, "title": {"english": "Alpha release", "native": "Alpha Shelf"}}],
                "Beta Shelf": [{"id": 22, "title": {"english": "Beta Shelf"}}, {"id": 23, "title": {"romaji": "Beta Shelf"}}],
                "Rate Shelf": [{"id": 24, "title": {"english": "Rate Shelf"}}],
                "Lantern Evening": [{"id": 25, "title": {"english": "Lantern Evening"}}],
                "Moonlit Notes": [{"id": 26, "title": {"english": "Moonlit Notes"}}],
            }
            result = json.dumps({"data": {"Page": {"media": matches.get(title, [])}}}).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(result)))
            self.end_headers()
            self.wfile.write(result)

        def log_message(self, *_args):
            pass

    queue = "/api/metadata-suggestions"
    data = fixture / "queue-data"
    password = uuid4().hex

    def settled(browser, count):
        for _ in range(120):
            rows = json.loads(browser.request(queue, 200))["items"]
            if len(rows) == count and all(row["candidates"] is not None and not row["queued"] for row in rows):
                return {row["title"]: row for row in rows}
            time.sleep(0.25)
        raise AssertionError(f"Queue did not settle: {rows}")

    with ThreadingHTTPServer(("127.0.0.1", 0), Provider) as service:
        thread = threading.Thread(target=service.serve_forever, daemon=True)
        thread.start()
        try:
            lookup = f"http://127.0.0.1:{service.server_port}/graphql"
            with start_app(executable, data, media, password, metadata_service=lookup) as address:
                wait_for_server(address)
                Browser(address).request(queue, 401)
                browser = Browser(address)
                browser.sign_in(password)
                root = browser.roots()[0]["id"]
                browser.scan(root)
                rows = settled(browser, 3)
                assert sorted(name for name, _ in calls) == ["Alpha Shelf", "Beta Shelf", "Gamma Shelf"]
                alpha = rows["Alpha Shelf"]
                assert alpha["candidates"][0]["matchingTitle"] == "Alpha Shelf"
                assert len(rows["Beta Shelf"]["candidates"]) == 2 and rows["Gamma Shelf"]["candidates"] == []
                close = browser.series(root, "?view=close&limit=1")
                assert close["total"] == 1 and close["items"][0]["title"] == "Alpha Shelf"
                assert close["items"][0]["aniList"]["close"] and close["items"][0]["aniList"]["metadata"]["providerId"] == 21
                assert not browser.files(root, "?view=library")["total"] and len(calls) == 3
                browser.scan(root)
                assert settled(browser, 3) == rows
                page = json.loads(browser.request(queue + "?limit=1", 200))
                assert page["total"] == 3 and page["items"] == [alpha]
                assert len(calls) == 3

            with start_app(executable, data, media, None, metadata_service=lookup) as address:
                wait_for_server(address)
                browser = Browser(address)
                browser.sign_in(password)
                assert settled(browser, 3) == rows
                visible = [{"rootId": item["rootId"], "seriesKey": item["seriesKey"]} for item in page["items"]]
                browser.request(queue + "/refresh", 400, method="POST", body={"items": visible})
                result = json.loads(browser.request(queue + "/refresh", 202, method="POST", token=browser.csrf(), body={"items": visible}))
                assert result == {"queued": 1}
                refreshed = settled(browser, 3)
                assert [name for name, _ in calls][3:] == ["Alpha Shelf"]
                assert refreshed["Alpha Shelf"]["revision"] != alpha["revision"]
                for title in ("Beta Shelf", "Gamma Shelf"):
                    assert refreshed[title] == rows[title]

                target = f"{queue}/{root}/{alpha['seriesKey']}"
                match = {"providerId": 21, "revision": alpha["revision"]}
                browser.request(target, 409, method="POST", token=browser.csrf(), body=match)
                match["revision"] = refreshed["Alpha Shelf"]["revision"]
                saved = json.loads(browser.request(target, 200, method="POST", token=browser.csrf(), body=match))
                assert saved["title"] == "Alpha release" and saved["providerId"] == 21
                published = browser.files(root, f"?view=library&seriesKey={alpha['seriesKey']}")
                assert published["total"] == 2 and all(file["associationConfirmed"] for file in published["items"])
                browser.request(target, 409, method="POST", token=browser.csrf(), body=match)
                assert [name for name, _ in calls][3:] == ["Alpha Shelf"]
                assert json.loads(browser.request(queue + "?limit=1", 200))["total"] == 2

                (media / "primary" / "Rate Shelf Vol.01.pdf").write_bytes(b"Newly arrived")
                browser.scan(root)
                rows = settled(browser, 3)
                assert rows["Rate Shelf"]["candidates"][0]["match"]["providerId"] == 24
                assert [name for name, _ in calls][4:] == ["Rate Shelf", "Rate Shelf"]
                assert calls[-1][1] - calls[-2][1] >= 3.5

                archive = next(folder["id"] for folder in browser.roots() if folder["id"] != root)
                (media / "archive" / "Beta Shelf Vol.03.pdf").write_bytes(b"Different release, same normalized title")
                browser.scan(archive)
                archive_beta = json.loads(browser.request(f"{queue}?root={archive}&limit=1", 200))["items"][0]
                assert archive_beta["candidates"] == rows["Beta Shelf"]["candidates"] and not archive_beta["queued"]
                assert len(calls) == 6

                identity_title = "Recovered Identity Shelf"
                identity_path = f"{identity_title} Vol.01.pdf"
                (media / "primary" / identity_path).write_bytes(b"Identity becomes available on the next scan")
                # Reproduce a published path whose previous scan could not hash its contents.
                with closing(sqlite3.connect(data / "organizer.sqlite")) as database, database:
                    database.execute("""
                        INSERT INTO library_file(root_id, relative_path, extension, size, modified_at, series_key, series_title)
                            SELECT root_id, ?, 'pdf', 10, modified_at, ?, ?
                            FROM library_file WHERE lower(root_id) = ? LIMIT 1
                        """, (identity_path, sha256(b"series\0recovered identity shelf").hexdigest(), identity_title, root))
                browser.scan(root)
                assert settled(browser, 5)[identity_title]["candidates"] == []
                assert [name for name, _ in calls][6:] == [identity_title]

                priority_titles = {"Page Window Apple", "Page Window Birch", "Page Window Cedar"}
                for title in priority_titles:
                    (media / "primary" / f"{title} Vol.01.pdf").write_bytes(title.encode())
                browser.scan(root)
                assert page_started.wait(timeout=8)
                running = calls[-1][0]
                wanted = max(priority_titles - {running}, key=lambda title: sha256(("series\0" + title.lower()).encode()).digest())
                page = json.loads(browser.request(queue + "?" + urlencode({"search": wanted, "limit": 1}), 200))
                (row,) = page["items"]
                assert row["title"] == wanted and row["queued"] and not row["pagePriority"]
                visible = {"items": [{"rootId": row["rootId"], "seriesKey": row["seriesKey"], "alreadyQueued": True}]}

                def prioritize():
                    return json.loads(browser.request(queue + "/refresh", 202, method="POST", token=browser.csrf(), body=visible))

                assert prioritize() == {"queued": 1}
                assert prioritize() == {"queued": 0}
                assert json.loads(browser.request(queue + "?" + urlencode({"search": wanted}), 200))["items"][0]["pagePriority"]
                release_page.set()
                settled(browser, 8)
                assert [name for name, _ in calls][7:] == [running, wanted, *(priority_titles - {running, wanted})]
                # A browser with an outdated queued row can only prioritize it, so completion wins this race.
                assert prioritize() == {"queued": 0}

            with start_app(executable, data, media, None, metadata_service=lookup) as address:
                wait_for_server(address)

                def manual_search(query):
                    browser = Browser(address)
                    browser.sign_in(password)
                    url = f"{LIBRARY_ROOTS}/{root}/series/{alpha['seriesKey']}/metadata/search?"
                    assert json.loads(browser.request(url + urlencode({"title": query}), 200)) == []

                with ThreadPoolExecutor(max_workers=2) as requests:
                    list(requests.map(manual_search, ["User Lookup One", "User Lookup Two"]))
                assert {name for name, _ in calls[-2:]} == {"User Lookup One", "User Lookup Two"}
                assert calls[-1][1] - calls[-2][1] >= 2.8

                browser = Browser(address)
                browser.sign_in(password)
                prefix = "(COMITIA101) [Studio (Artist)] "
                note = " & [English] [Translator]"
                old_title = prefix + "Lantern Evening" + note
                old_key = sha256(old_title.encode()).hexdigest()
                old_path = old_title + ".pdf"
                (media / "primary" / old_path).write_bytes(b"Previously indexed raw convention title")
                same_key_title = "Moonlit Notes"
                same_key = sha256(b"series\0moonlit notes").hexdigest()
                second_path = "(C103) [Studio (Artist)] " + same_key_title + note + ".pdf"
                (media / "primary" / second_path).write_bytes(b"Previously indexed bare title with a separator")
                with closing(sqlite3.connect(data / "organizer.sqlite")) as database, database:
                    for key, title, path in ((old_key, old_title, old_path), (same_key, same_key_title + " &", second_path)):
                        digest = sha256((media / "primary" / path).read_bytes()).digest()
                        content = database.execute("INSERT INTO library_content(sha256) VALUES (?) RETURNING id", (digest,)).fetchone()[0]
                        database.execute("""
                            INSERT INTO library_file(root_id, relative_path, extension, size, modified_at, series_key, series_title, content_id)
                            SELECT root_id, ?, 'pdf', 10, modified_at, ?, ?, ?
                            FROM library_file WHERE lower(root_id) = ? LIMIT 1
                            """, (path, key, title, content, root))
                        database.execute("""
                            INSERT INTO metadata_suggestion(series_key, title, revision, queued, requested_at, candidates)
                            VALUES (?, ?, lower(hex(randomblob(16))), 0, unixepoch(), '[]')
                            """, (key, title))

                before = len(calls)
                (legacy,) = browser.series(root, f"?view=all&seriesKey={old_key}")["items"]
                assert legacy["title"] == old_title and legacy["lookupTitle"] == "Lantern Evening"
                (file,) = browser.files(root, f"?view=all&seriesKey={old_key}")["items"]
                assert file["relativePath"] == old_path and file["lookupTitle"] == "Lantern Evening"
                (row,) = json.loads(browser.request(queue + "?search=Lantern+Evening", 200))["items"]
                assert row["lookupTitle"] == "Lantern Evening" and row["candidates"] == []
                browser.request(queue + "/refresh", 202, method="POST", token=browser.csrf(),
                                body={"items": [{"rootId": root, "seriesKey": old_key}]})
                legacy_result = settled(browser, 10)[old_title]
                assert legacy_result["candidates"][0]["matchingTitle"] == "Lantern Evening"
                assert [name for name, _ in calls[before:]] == ["Lantern Evening"]
                browser.request(f"{queue}/{root}/{old_key}", 200, method="POST", token=browser.csrf(),
                                body={"revision": legacy_result["revision"], "providerId": 25})

                browser.scan(root)
                rescanned = settled(browser, 9)[same_key_title]
                assert rescanned["seriesKey"] == same_key and rescanned["candidates"][0]["matchingTitle"] == same_key_title
                assert browser.files(root, f"?view=library&seriesKey={old_key}")["items"][0]["associationConfirmed"]
                assert [name for name, _ in calls[before:]] == ["Lantern Evening", same_key_title]
        finally:
            release_page.set()
            service.shutdown()
            thread.join()


def verify_maintenance_resets(executable, fixture):
    media, data = fixture / "reset-media", fixture / "reset-data"
    create_managed_fixture(media)
    source_file = media / "primary" / "Chapter 1.cbz"
    target_file = media / "archive" / "Chapter 1.cbz"
    contents = source_file.read_bytes()
    password = uuid4().hex
    endpoint = "/api/maintenance/resets"
    operations = "/api/file-operations"

    def preview(owner, level, roots=(), expected=200):
        return json.loads(owner.request(endpoint + "/preview", expected, method="POST", token=owner.csrf(),
                                        body={"level": level, "rootIds": list(roots)}))

    def apply(owner, plan, expected=200, text=None):
        return json.loads(owner.request(endpoint + "/apply", expected, method="POST", token=owner.csrf(),
                                        body={"token": plan["token"], "confirmation": text if text is not None else plan["confirmation"]}))

    with start_app(executable, data, media, password) as url:
        wait_for_server(url)
        Browser(url).request(endpoint, 401)
        owner = Browser(url)
        owner.sign_in(password)
        source, target = owner.roots()
        source_id, target_id = source["id"], target["id"]
        for folder, kind, name in [(source, "both", "Reset source"), (target, "library", "Retained target")]:
            owner.request(f"{LIBRARY_ROOTS}/{folder['id']}", 200, method="PUT", token=owner.csrf(),
                          body={"path": folder["path"], "kind": kind, "name": name, "managed": True})
        owner.request(f"{LIBRARY_ROOTS}/{source_id}/intake-target", 204, method="PUT", token=owner.csrf(),
                      body={"targetRoot": target_id})
        owner.scan(source_id)
        (file,) = owner.files(source_id)["items"]
        series = f"{LIBRARY_ROOTS}/{source_id}/series/{file['seriesKey']}"
        owner.request(series + "/metadata", 200, method="PUT", token=owner.csrf(), body={"title": "Reset Fixture", "kind": "manga"})
        transfer = json.loads(owner.request(operations + "/preview", 200, method="POST", token=owner.csrf(), body={
            "sourceRoot": source_id, "targetRoot": target_id, "fileId": file["id"],
            "targetPath": target_file.name, "intakeTransfer": True,
        }))
        operation = f"{operations}/{transfer['id']}"
        owner.request(operation + "/apply", 200, method="POST", token=owner.csrf())
        recovery = data / "file-recovery" / f"{UUID(transfer['id']).hex}.backup"
        assert recovery.read_bytes() == contents
        source_file.write_bytes(contents)
        owner.scan(source_id)

        book = "/api/reader/" + file["contentId"]
        owner.request(book + "/progress", 200, method="PUT", token=owner.csrf(),
                      body={"updateId": str(uuid4()), "baseRevision": 0, "pageIndex": 0})
        cancelled = preview(owner, "progress")
        assert any(change["count"] for change in cancelled["affected"])
        assert json.loads(owner.request(endpoint, 200)) == []
        apply(owner, cancelled, 400, "WRONG")
        owner.request(book + "/progress", 200, method="PUT", token=owner.csrf(),
                      body={"updateId": str(uuid4()), "baseRevision": 1, "pageIndex": 0, "completed": True})
        apply(owner, cancelled, 409)
        assert json.loads(owner.request(book, 200))["progress"]["revision"] == 2

        plan = preview(owner, "progress")
        backup_root = data / "maintenance-backups"
        backup_root.write_text("Fixture destination creation failure")
        try:
            apply(owner, plan, 503)
            assert json.loads(owner.request(book, 200))["progress"]["revision"] == 2
        finally:
            backup_root.unlink()
        progress = apply(owner, preview(owner, "progress"))
        backup_path = backup_root / progress["backupDirectory"]
        assert (backup_path / "manifest.json").is_file()
        assert json.loads(owner.request(book, 200))["progress"]["revision"] == 0
        with closing(sqlite3.connect(backup_path / "organizer.sqlite")) as backup:
            assert backup.execute("PRAGMA integrity_check").fetchone() == ("ok",)
            assert backup.execute("SELECT revision FROM reading_progress").fetchone() == (2,)

        with closing(sqlite3.connect(data / "organizer.sqlite")) as db, db:
            db.execute("UPDATE library_root SET state = 'queued' WHERE lower(id) = ?", (source_id,))
        preview(owner, "catalog", [source_id], 409)
        with closing(sqlite3.connect(data / "organizer.sqlite")) as db, db:
            db.execute("UPDATE library_root SET state = 'complete' WHERE lower(id) = ?", (source_id,))

        catalog = apply(owner, preview(owner, "catalog", [source_id]))
        assert owner.files(source_id)["total"] == 0
        assert owner.files(target_id, "?view=library")["total"] == 1
        assert source_file.read_bytes() == target_file.read_bytes() == contents
        assert recovery.read_bytes() == contents

        configuration = apply(owner, preview(owner, "configuration", [source_id]))
        saved = next(folder for folder in owner.roots() if folder["id"] == source_id)
        assert saved["kind"] == "library" and not saved["managed"] and saved["intakeTargetId"] is None
        assert saved["name"] is None and Path(saved["path"]).samefile(source_file.parent)
        assert recovery.read_bytes() == contents
        assert [record["id"] for record in json.loads(owner.request(endpoint, 200))] == [configuration["id"], catalog["id"], progress["id"]]

    with start_app(executable, data, media, None) as url:
        wait_for_server(url)
        owner = Browser(url)
        owner.sign_in(password)
        assert len(json.loads(owner.request(endpoint, 200))) == 3
        assert json.loads(owner.request(book, 200))["progress"]["revision"] == 0

    restored_data = fixture / "reset-restore"
    copytree(backup_path, restored_data)
    with start_app(executable, restored_data, media, None) as url:
        wait_for_server(url)
        owner = Browser(url)
        owner.sign_in(password)
        assert json.loads(owner.request(book, 200))["progress"]["revision"] == 2
        assert any(item["id"] == transfer["id"] for item in json.loads(owner.request(operations, 200)))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--app", type=Path, help="published executable")
    mode.add_argument("--image", help="built Docker image")
    target = parser.parse_args()
    with tempfile.TemporaryDirectory() as fixture:
        media = Path(fixture) / "media"
        create_media_fixture(media)
        managed_media = Path(fixture) / "managed-media"
        create_managed_fixture(managed_media)
        if target.app:
            executable = str(target.app.resolve())
            verify_web_setup(executable, Path(fixture), media)
            verify_external_metadata(executable, Path(fixture))
            verify_metadata_queue(executable, Path(fixture))
            verify_maintenance_resets(executable, Path(fixture))
            data_dir = Path(fixture) / "data"
            smoke(lambda password: start_app(executable, data_dir, media, password), media)
            managed_data = Path(fixture) / "managed-data"
            verify_managed_files(lambda password: start_app(executable, managed_data, managed_media, password), managed_media,
                                 data_directory=managed_data)
        else:
            volume = f"manga-smoke-{uuid4().hex}"
            subprocess.run(["docker", "volume", "create", volume], check=True, stdout=subprocess.DEVNULL)
            try:
                smoke(lambda password: start_container(target.image, volume, media, password), media)
            finally:
                subprocess.run(["docker", "volume", "rm", volume], check=True, stdout=subprocess.DEVNULL)
            managed_volume = f"manga-managed-{uuid4().hex}"
            subprocess.run(["docker", "volume", "create", managed_volume], check=True, stdout=subprocess.DEVNULL)
            try:
                verify_managed_files(lambda password: start_container(target.image, managed_volume, managed_media, password, readonly=False),
                                     managed_media, shared_permissions=True)
            finally:
                subprocess.run(["docker", "volume", "rm", managed_volume], check=True, stdout=subprocess.DEVNULL)
    print("Passed WebUI assets, authentication, CSRF, scans, covers, metadata, reader handoff, and reviewed file operations")
