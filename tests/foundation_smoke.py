"""Exercise the published server through its browser-facing HTTP API."""

import argparse
from contextlib import closing, contextmanager
from hashlib import sha256
from http.cookiejar import CookieJar
from pathlib import Path
import json
import os
import re
import socket
import sqlite3
import struct
import subprocess
import tempfile
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
def start_app(executable, data_dir, media, password, *, expect_setup=False):
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
def start_container(image, volume, media, password):
    name = f"manga-smoke-{uuid4().hex}"
    command = ["docker", "run", "--detach", "--name", name, "-p", "127.0.0.1::8787",
               "--mount", f"type=volume,source={volume},target=/config",
               "--mount", f"type=bind,source={media},target=/media,readonly",
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
            DROP INDEX library_file_content;
            DROP INDEX library_file_series;
            ALTER TABLE library_file DROP COLUMN content_id;
            ALTER TABLE library_file DROP COLUMN series_key;
            ALTER TABLE library_file DROP COLUMN series_title;
            DROP TABLE library_content;
            ALTER TABLE library_folder RENAME TO current_library_folder;
            CREATE TABLE library_folder (
                root_id TEXT NOT NULL PRIMARY KEY REFERENCES library_root(id),
                path TEXT NOT NULL,
                name TEXT,
                kind TEXT NOT NULL CHECK (kind IN ('library', 'import'))
            );
            INSERT INTO library_folder SELECT * FROM current_library_folder ORDER BY rowid;
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
                 "[Group B] Northbound Chapter 04.cbz", "Incoming/001.cbz", "Incoming/002.cbz",
                 "Different Title 2.cbz", "Different Title 3.cbz", "Different Title 2 (2022).cbz"]
    numbered_releases = {
        "A Kiss with a Cat": [f"A Kiss with a Cat v{n:02} ({2022 if n < 5 else 2023}) (Digital) (1r0n).cbz"
                             for n in range(1, 6)],
        "Akumetsu": [f"Akumetsu - Tome {n:02} [CBR] [Scan DP] [FR] [1500x1200].cbr" for n in range(1, 8)],
        "Undead Unluck": [f"Undead Unluck {n} (2022) (Digital) (1r0n).cbz" for n in range(107, 119)],
    }
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
    assert groups["total"] == len(by_title) == 11
    assert by_title[series_title]["fileCount"] == 53
    assert by_title["Northbound"]["fileCount"] == 4
    assert by_title["Incoming/001.cbz"]["fileCount"] == by_title["Incoming/002.cbz"]["fileCount"] == 1
    assert by_title["Different Title 2"]["key"] != by_title["Different Title 3"]["key"]
    assert by_title["Different Title 2 (2022)"]["fileCount"] == 1
    assert owner.series(root_id, "?limit=1") == {"total": 11, "items": groups["items"][:1]}
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


def verify_cover_art(owner, root_id, path):
    sidecar_bytes, first_page, later_page = synthetic_png(90, 20, 220), synthetic_png(32, 160, 122), synthetic_png(221, 42, 70)
    src = path / "Éclipse"
    src.mkdir()
    with ZipFile(src / "Éclipse v01.cbz", "w", compression=ZIP_DEFLATED) as archive:
        archive.writestr("./unsafe.png", later_page)
        archive.writestr("pages/1.png", b"Corrupt first entry")
        archive.writestr("pages/10.png", later_page)
        archive.writestr("pages/2.png", first_page)
    (src / "cover.png").write_bytes(sidecar_bytes)
    owner.scan(root_id)
    assert any(root["id"] == root_id and root["hasCovers"] for root in owner.roots())
    (series,) = owner.series(root_id, "?search=eclipse&coverOnly=true")["items"]
    assert series["title"] == "Éclipse" and series["hasCover"]
    url = f"{LIBRARY_ROOTS}/{root_id}/series/{series['key']}/cover"
    assert owner.request(url, 200) == sidecar_bytes
    assert owner.headers.get_content_type() == "image/png" and owner.headers["X-Content-Type-Options"] == "nosniff"
    Browser(owner.url).request(url, 401)

    (src / "cover.png").write_bytes(b"Invalid cover")
    owner.request(url, 404)
    owner.scan(root_id)
    assert owner.request(url, 200) == first_page
    all_series = owner.series(root_id, "?sort=files")["items"]
    assert all_series[0]["fileCount"] == 53 and any(not grouped["hasCover"] for grouped in all_series)
    owner.request(f"{LIBRARY_ROOTS}/{root_id}/series?sort=unknown", 400)


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


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--app", type=Path, help="published executable")
    mode.add_argument("--image", help="built Docker image")
    target = parser.parse_args()
    with tempfile.TemporaryDirectory() as fixture:
        media = Path(fixture) / "media"
        create_media_fixture(media)
        if target.app:
            executable = str(target.app.resolve())
            verify_web_setup(executable, Path(fixture), media)
            data_dir = Path(fixture) / "data"
            smoke(lambda password: start_app(executable, data_dir, media, password), media)
        else:
            volume = f"manga-smoke-{uuid4().hex}"
            subprocess.run(["docker", "volume", "create", volume], check=True, stdout=subprocess.DEVNULL)
            try:
                smoke(lambda password: start_container(target.image, volume, media, password), media)
            finally:
                subprocess.run(["docker", "volume", "rm", volume], check=True, stdout=subprocess.DEVNULL)
    print("Passed WebUI assets, authentication, CSRF, dual-purpose folders, series groups, cover art, file identity, and restart persistence")
