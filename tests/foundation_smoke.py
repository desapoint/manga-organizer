"""Exercise the published server through its browser-facing HTTP API."""

import argparse
from base64 import b64decode
from contextlib import closing, contextmanager
from hashlib import sha256
from http.cookiejar import CookieJar
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json
import os
import re
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
        if body is not None:
            headers["Content-Type"] = "application/json"
            body = json.dumps(body).encode()
        request = Request(self.url + path, data=body, headers=headers, method=method)
        try:
            response = self.opener.open(request, timeout=5)
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

    def scan(self, root_id, expected_state="complete"):
        self.request(f"{LIBRARY_ROOTS}/{root_id}/scan", 202, method="POST", token=self.csrf())
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
    if metadata_service is not None:
        env["MANGA_ORGANIZER_METADATA_SERVICE_URL"] = metadata_service
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
            DROP TABLE edition_volume_release;
            DROP TABLE edition_volume_manifest;
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
    assert groups["total"] == len(by_title) == 14
    assert by_title[series_title]["fileCount"] == 53
    assert by_title["Northbound"]["fileCount"] == 6
    assert by_title["Incoming/001.cbz"]["fileCount"] == by_title["Incoming/002.cbz"]["fileCount"] == 1
    assert by_title["Incoming/Ch.004 - Unknown story.cbz"]["fileCount"] == 1
    assert by_title["Chapters/Ch.004 - Unknown story.cbz"]["fileCount"] == 1
    assert by_title["Another Series"]["fileCount"] == 1
    assert by_title["Different Title 2"]["key"] != by_title["Different Title 3"]["key"]
    assert by_title["Different Title 2 (2022)"]["fileCount"] == 1
    assert owner.series(root_id, "?limit=1") == {"total": 14, "items": groups["items"][:1]}
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
    (src / "cover-2.png").write_bytes(later_page)
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
    (src / "cover-2.png").unlink()
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


def verify_external_metadata(executable, fixture, media):
    (media / "primary" / "Issue 01.pdf").write_bytes(b"Unique synthetic issue for matching tests")
    cover = "https://s4.anilist.co/file/anilistcdn/media/manga/cover/medium/fixture.jpg"
    media_result = {"id": 123, "title": {"english": "Synthetic provider title", "native": "本"},
                    "description": "Metadata &amp; tags<br><br><i>Second paragraph</i>", "status": "FINISHED", "chapters": 12, "volumes": 3,
                    "format": "ONE_SHOT", "countryOfOrigin": "JP", "startDate": {"year": 2011},
                    "endDate": {"year": 2012}, "genres": ["Drama", "drama"],
                    "coverImage": {"medium": cover}, "staff": {"edges": [
                        {"role": "Story & Art", "node": {"name": {"full": "Fixture Author"}}}]},
                    "tags": [{"name": "Doujinshi"}, {"name": "Hidden outcome", "isMediaSpoiler": True}]}
    no_cover = {"id": 456, "title": {"romaji": "Cover fallback"},
                "coverImage": {"medium": "https://s4.anilist.co.invalid/file/anilistcdn/media/manga/cover/fake.jpg"}}

    class Provider(BaseHTTPRequestHandler):
        def do_POST(self):
            request = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            variables = request["variables"]
            if not all(field in request["query"] for field in ("coverImage", "genres", "isMediaSpoiler", "staff")):
                response = {"errors": [{"message": "Missing provider fields"}]}
            elif variables == {"title": "Issue 01"}:
                response = {"data": {"Page": {"media": [media_result, no_cover]}}}
            elif variables == {"id": 123}:
                response = {"data": {"Media": media_result}}
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
                corrected = json.loads(browser.request(f"{url}/metadata", 200, method="PUT", token=browser.csrf(), body=edit))
                assert corrected["title"] == edit["title"] and corrected["providerUrl"] == result["providerUrl"]
                assert corrected["providerDetails"] == result["providerDetails"]
                assert json.loads(browser.request(f"{url}/details", 200))["metadata"] == corrected
                collections = "/api/library/collections"
                assert json.loads(browser.request(f"{LIBRARY_ROOTS}/{root_id}/tags", 200)) == ["Doujinshi"]
                tagged = browser.series(root_id, "?includeTag=doujinshi")
                assert tagged["total"] == 1, tagged
                assert browser.series(root_id, "?includeTag=Doujinshi&includeTag=Adventure")["total"] == 0
                assert browser.series(root_id, "?search=My+corrected+title&excludeTag=Doujinshi")["total"] == 0
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
        finally:
            service.shutdown()
            server.join(timeout=5)


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
    owner.scan(archive["id"])
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
    assert json.loads(owner.request(f"{group_url}/details", 200))["availableChapters"] == 5
    releases = json.loads(owner.request(f"{group_url}/chapter-releases?volume=2&chapter=3", 200))
    assert {item["contentId"] for item in releases} == {files[range_file.name]["contentId"], files[standalone.name]["contentId"]}
    mark = {"volume": "02", "chapter": "03", "completed": True}
    owner.request(f"{group_url}/chapter-progress", 400, method="PUT", body=mark)
    owner.request(f"{group_url}/chapter-progress", 204, method="PUT", token=owner.csrf(), body=mark)
    marked = json.loads(owner.request(f"{group_url}/details", 200))
    assert marked["readChapters"] == 1 and marked["chapters"][0]["completed"]
    assert all(item["pageIndex"] is None for item in json.loads(owner.request(f"{group_url}/chapter-releases?volume=2&chapter=3", 200)))
    owner.request(f"{group_url}/chapter-progress", 204, method="PUT", token=owner.csrf(), body=dict(mark, completed=False))
    assert json.loads(owner.request(f"{group_url}/details", 200))["readChapters"] == 0

    candidates = json.loads(owner.request(f"{group_url}/edition-candidates?volume=5", 200))
    assert len(candidates) == 1 and candidates[0]["unresolvedVolume"]
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

        review_duplicates(owner, primary["id"], original.parent)

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


def review_duplicates(owner, root, directory):
    volume = directory / "Safety Fixture Vol.01 Ch.1-3.cbz"
    chapter = directory / "Safety Fixture Vol.01 Ch.1.cbz"
    copy = directory / "Safety Fixture Vol.01 Ch.1-3 [Variant 1].cbz"
    alias = directory / "Safety Fixture Vol.01 Ch.1 [Variant 1].cbz"
    for index, path in enumerate((volume, chapter)):
        with ZipFile(path, "w", compression=ZIP_DEFLATED) as archive:
            archive.writestr("1.png", synthetic_png(125, index + 5, 35))
    copy.write_bytes(volume.read_bytes())
    os.link(chapter, alias)
    owner.scan(root)
    files = {item["relativePath"]: item for item in owner.files(root)["items"]}
    operations = "/api/file-operations"

    def compare(path):
        return json.loads(owner.request(f"{operations}/duplicates/{root}/{files[path.name]['id']}", 200))["comparisons"]

    def quarantine(source, retain, status):
        return json.loads(owner.request(operations + "/quarantine-preview", status, method="POST", token=owner.csrf(), body={
            "rootId": root, "sourceFile": files[source.name]["id"], "keepFile": files[retain.name]["id"],
        }))

    comparisons = {item["candidate"]["relativePath"]: item for item in compare(volume)}
    overlap = comparisons[chapter.name]
    assert overlap["kind"] == "overlap" and not overlap["canQuarantine"]
    assert [item["chapter"] for item in overlap["shared"]] == ["1"]
    assert [item["chapter"] for item in overlap["sourceOnly"]] == ["2", "3"]
    assert comparisons[copy.name]["canQuarantine"]
    assert any(item["kind"] == "hardlink" and not item["canQuarantine"] for item in compare(chapter))
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
    result = json.loads(owner.request(url + "/apply", 200, method="POST", token=owner.csrf()))
    assert result["kind"] == "quarantine" and result["state"] == "completed"
    assert not copy.exists() and volume.read_bytes() == backup_source
    assert Path(result["target"]).name.endswith(".recovery")
    assert copy.name not in {item["relativePath"] for item in owner.files(root)["items"]}
    owner.scan(root)
    assert owner.files(root, "?search=recovery")["total"] == 0
    owner.request(url + "/restore", 200, method="POST", token=owner.csrf())
    assert copy.read_bytes() == volume.read_bytes() == backup_source

    for path in (volume, chapter, copy, alias):
        path.unlink()
    owner.scan(root)


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
            verify_external_metadata(executable, Path(fixture), media)
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
