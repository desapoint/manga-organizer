"""Verify a disposable Suwayomi instance with the generated synthetic source."""

import argparse
import base64
import gzip
import json
import time
from concurrent.futures import ThreadPoolExecutor
from http.cookiejar import CookieJar
from pathlib import Path
from urllib.request import HTTPCookieProcessor, Request, build_opener

PACKAGE = "org.mangaorganizer.audit.fixture"
SOURCE_ID = "7200000000000000123"
AUTH = "Basic " + base64.b64encode(b"audit-fixture:synthetic-local-password").decode()


class Engine:
    def __init__(self, port):
        self.endpoint = f"http://127.0.0.1:{port}/api/graphql"
        self.opener = build_opener(HTTPCookieProcessor(CookieJar()))

    def request(self, body, content_type):
        request = Request(self.endpoint, data=body, headers={
            "Authorization": AUTH, "Content-Type": content_type,
        })
        with self.opener.open(request, timeout=40) as response:
            return json.load(response)

    def graphql(self, query):
        return self.request(json.dumps({"query": query}).encode(), "application/json")

    def export_backup(self, manga_id, output):
        favorite = self.graphql(f"mutation {{updateManga(input:{{id:{manga_id},patch:{{inLibrary:true}}}}){{manga{{id inLibrary}}}}}}")
        assert favorite["data"]["updateManga"]["manga"]["inLibrary"], favorite
        result = self.graphql("mutation {createBackup(input:{includeChapters:true}){url}}")
        path = result["data"]["createBackup"]["url"]
        assert path.startswith("/api/graphql/files/backup/"), result
        request = Request(self.endpoint.removesuffix("/api/graphql") + path, headers={"Authorization": AUTH})
        with self.opener.open(request, timeout=40) as response:
            backup = response.read()
        assert b"Synthetic Audit Work" in gzip.decompress(backup)
        output.write_bytes(backup)
        print("Exported a Suwayomi gzip backup containing the synthetic title")

    def upload_extension(self, package):
        query = """mutation($file: Upload!) {
          installExternalExtension(input: {extensionFile: $file}) {
            extension { pkgName isInstalled versionName }
          }
        }"""
        boundary = "MangaOrganizerSyntheticFixture"
        operations = json.dumps({"query": query, "variables": {"file": None}}).encode()
        mime = "application/vnd.android.package-archive" if package.suffix == ".apk" else "application/java-archive"
        parts = [
            ('name="operations"', "application/json", operations),
            ('name="map"', "application/json", json.dumps({"0": ["variables.file"]}).encode()),
            (f'name="0"; filename="{package.name}"', mime, package.read_bytes()),
        ]
        body = bytearray()
        for disposition, content_type, content in parts:
            body.extend(f"--{boundary}\r\nContent-Disposition: form-data; {disposition}\r\n".encode())
            body.extend(f"Content-Type: {content_type}\r\n\r\n".encode())
            body.extend(content + b"\r\n")
        body.extend(f"--{boundary}--\r\n".encode())
        return self.request(bytes(body), f"multipart/form-data; boundary={boundary}")


def wait_for_engine(engine):
    for _ in range(45):
        try:
            result = engine.graphql("{aboutServer{version}}")
            return result["data"]["aboutServer"]["version"]
        except (OSError, KeyError) as error:
            last_error = error
            time.sleep(1)
    raise RuntimeError("Suwayomi did not become ready") from last_error


def verify_download(engine, fixture_dir, fixture_package=None):
    extension = engine.upload_extension(fixture_package or fixture_dir / "audit-fixture-1.jar")
    installed = extension.get("data", {}).get("installExternalExtension")
    assert installed and installed["extension"] == {
        "pkgName": PACKAGE, "isInstalled": True, "versionName": "1.4.1",
    }, extension
    search = engine.graphql(f'''mutation {{
      fetchSourceManga(input:{{source:"{SOURCE_ID}",type:SEARCH,page:1,query:"Audit"}}) {{ mangas {{id title source{{id}}}} }}
    }}''')
    work = search["data"]["fetchSourceManga"]["mangas"][0]
    assert work["title"] == "Synthetic Audit Work" and work["source"]["id"] == SOURCE_ID, search
    listing = engine.graphql(f'''mutation {{
      fetchMangaAndChapters(input:{{id:{work['id']},fetchManga:true,fetchChapters:true}}) {{ chapters {{id chapterNumber}} }}
    }}''')
    chapter = listing["data"]["fetchMangaAndChapters"]["chapters"][0]
    assert chapter["chapterNumber"] == 12.5, listing
    chapter_id = chapter["id"]
    enqueue = engine.graphql(f'''mutation {{
      enqueueChapterDownload(input:{{id:{chapter_id}}}) {{downloadStatus{{queue{{chapter{{id}}}}}}}}
    }}''')
    queued = enqueue["data"]["enqueueChapterDownload"]["downloadStatus"]["queue"]
    assert any(item["chapter"]["id"] == chapter_id for item in queued), enqueue

    with ThreadPoolExecutor(max_workers=1) as executor:
        start = executor.submit(engine.graphql, "mutation {startDownloader(input:{}){downloadStatus{state}}}")
        for _ in range(35):
            state = engine.graphql(f"{{chapter(id:{chapter_id}){{isDownloaded pageCount}}}}")
            if state.get("data", {}).get("chapter", {}).get("isDownloaded"):
                assert state["data"]["chapter"]["pageCount"] == 1, state
                break
            time.sleep(1)
        else:
            raise RuntimeError(f"Synthetic chapter did not download. Last status: {state}")
        result = start.result()
        if result.get("errors"):
            assert all("Timed out waiting" in item["message"] for item in result["errors"]), result
            print("startDownloader reported a GraphQL timeout after the chapter was downloaded")
    print(f"Synthetic chapter {chapter_id} downloaded through source {SOURCE_ID}")
    return work["id"]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("port", type=int)
    parser.add_argument("fixture_dir", type=Path)
    args = parser.parse_args()
    engine = Engine(args.port)
    assert wait_for_engine(engine) == "v2.4.2373"
    verify_download(engine, args.fixture_dir)
