"""Run the synthetic qualification on Windows or an amd64 Linux Docker host."""

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import uuid
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from zipfile import ZipFile

from make_fixture import make_fixture
from probe import Engine, verify_download, wait_for_engine

ENGINE_IMAGE = "ghcr.io/suwayomi/suwayomi-server@sha256:f87c945e76240adcb305f75c6fdf111d3917cd11834bdb1db5e95796778d4260"
PYTHON_IMAGE = "docker.io/library/python@sha256:191083cdf6c7bda8c76158983163ead5d5cf7b77cf3033e805bb0f5296f51b4b"
FIXTURE_PORT = 14569
ENGINE_PORT = 14568


def filesystem_probe(root):
    original = root / "Chapter.cbz"
    renamed = root / "chapter.cbz"
    original.write_bytes(b"synthetic-archive")
    original.rename(renamed)
    assert "chapter.cbz" in os.listdir(root)
    link = root / "alias.cbz"
    copy = root / "copy.cbz"
    os.link(renamed, link)
    shutil.copyfile(renamed, copy)
    assert os.path.samefile(renamed, link) and not os.path.samefile(renamed, copy)
    assert copy.read_bytes() == renamed.read_bytes()
    try:
        os.link(renamed, copy)
    except FileExistsError:
        assert copy.read_bytes() == b"synthetic-archive"
    else:
        raise AssertionError("An exclusive hardlink replaced an existing file")
    print("Case-only rename, hardlink identity, exact-copy distinction and no clobber passed")


def verify_page(downloads, fixture):
    pages = list(downloads.rglob("001.png"))
    assert len(pages) == 1 and pages[0].read_bytes() == (fixture / "site/001.png").read_bytes(), pages
    assert pages[0].with_name("ComicInfo.xml").is_file()
    print("One matching PNG and ComicInfo.xml on disk")


def verify_restart(engine):
    assert wait_for_engine(engine) == "v2.4.2373"
    state = engine.graphql('{extension(pkgName:"org.mangaorganizer.audit.fixture"){isInstalled versionName}}')
    assert state["data"]["extension"] == {"isInstalled": True, "versionName": "1.4.1"}, state


def windows(archive, gradle_lib, root):
    with ZipFile(archive) as package:
        package.extractall(root / "engine")
    jar = next((root / "engine").rglob("Suwayomi-Server.jar"))
    java = next((root / "engine").rglob("java.exe"))
    fixture = root / "fixture"
    data = root / "data"
    data.mkdir()
    make_fixture(jar, gradle_lib, fixture)
    filesystem_probe(data)
    engine = Engine(ENGINE_PORT)
    command = [str(java), "-Djava.awt.headless=true", f"-Dsuwayomi.tachidesk.config.server.rootDir={data}",
               "-Dsuwayomi.tachidesk.config.server.ip=127.0.0.1", f"-Dsuwayomi.tachidesk.config.server.port={ENGINE_PORT}",
               "-Dsuwayomi.tachidesk.config.server.authMode=BASIC_AUTH", "-Dsuwayomi.tachidesk.config.server.authUsername=audit-fixture",
               "-Dsuwayomi.tachidesk.config.server.authPassword=synthetic-local-password", "-Dsuwayomi.tachidesk.config.server.webUIEnabled=false",
               "-Dsuwayomi.tachidesk.config.server.initialOpenInBrowserEnabled=false", "-Dsuwayomi.tachidesk.config.server.systemTrayEnabled=false",
               "-Dsuwayomi.tachidesk.config.server.downloadAsCbz=false", "-jar", str(jar)]
    with ThreadingHTTPServer(("127.0.0.1", FIXTURE_PORT), partial(SimpleHTTPRequestHandler, directory=fixture / "site")) as provider:
        thread = threading.Thread(target=provider.serve_forever, daemon=True)
        thread.start()
        try:
            with (root / "engine.log").open("w+") as log:
                process = subprocess.Popen(command, cwd=jar.parent.parent, stdout=log, stderr=subprocess.STDOUT)
                try:
                    assert wait_for_engine(engine) == "v2.4.2373"
                    verify_download(engine, fixture)
                    verify_page(data / "downloads", fixture)
                except Exception:
                    log.seek(0)
                    print(log.read()[-12000:], file=sys.stderr)
                    raise
                finally:
                    process.terminate()
                    process.wait(timeout=30)
        finally:
            provider.shutdown()
            thread.join()


def docker(*arguments, check=True):
    return subprocess.run(["docker", *arguments], check=check, text=True, stdout=subprocess.PIPE).stdout.strip()


def linux(gradle_lib, root):
    run = "manga-audit-" + uuid.uuid4().hex[:10]
    engine, provider, config, media = (f"{run}-{name}" for name in ("engine", "provider", "config", "media"))
    docker("network", "create", run)
    try:
        docker("volume", "create", config)
        docker("volume", "create", media)
        docker("run", "--rm", "-v", f"{config}:/config", "-v", f"{media}:/media", PYTHON_IMAGE,
               "python", "-c", "import os\nos.mkdir('/media/downloads')\n"
               "for path in ('/config', '/media', '/media/downloads'):\n    os.chown(path, 99, 100)")
        docker("create", "--name", engine, "--network", run, "--user", "99:100", "-p", f"127.0.0.1:{ENGINE_PORT}:4567",
               "-v", f"{config}:/home/suwayomi/.local/share/Tachidesk", "-v", f"{media}:/media",
               "-e", "AUTH_MODE=basic_auth", "-e", "AUTH_USERNAME=audit-fixture", "-e", "AUTH_PASSWORD=synthetic-local-password",
               "-e", "KCEF_ENABLED=false", "-e", "WEB_UI_ENABLED=false", "-e", "DOWNLOAD_AS_CBZ=false",
               "-e", f"JDK_JAVA_OPTIONS=-Dmanga.audit.fixtureUrl=http://{provider}:{FIXTURE_PORT} "
                     "-Dsuwayomi.tachidesk.config.server.downloadsPath=/media/downloads",
               ENGINE_IMAGE)
        jar = root / "Suwayomi-Server.jar"
        docker("cp", f"{engine}:/home/suwayomi/startup/tachidesk_latest.jar", str(jar))
        fixture = root / "fixture"
        make_fixture(jar, gradle_lib, fixture)
        docker("run", "-d", "--name", provider, "--network", run, "--mount",
               f"type=bind,source={fixture / 'site'},target=/site,readonly", PYTHON_IMAGE,
               "python", "-m", "http.server", str(FIXTURE_PORT), "--directory", "/site")
        docker("start", engine)
        assert docker("exec", engine, "id", "-u") == "99"
        assert docker("exec", engine, "id", "-g") == "100"
        api = Engine(ENGINE_PORT)
        assert wait_for_engine(api) == "v2.4.2373"
        verify_download(api, fixture)
        downloaded = root / "docker-downloads"
        docker("cp", f"{engine}:/media/downloads", str(downloaded))
        verify_page(downloaded, fixture)
        docker("restart", engine)
        verify_restart(Engine(ENGINE_PORT))
        persisted = root / "docker-persisted"
        docker("cp", f"{engine}:/media/downloads", str(persisted))
        verify_page(persisted, fixture)
        print("Docker engine ran at UID:GID 99:100 and kept the fixture across restart")
    except Exception:
        print(docker("logs", "--tail", "50", engine, check=False), file=sys.stderr)
        raise
    finally:
        docker("rm", "-f", engine, provider, check=False)
        docker("volume", "rm", config, media, check=False)
        docker("network", "rm", run, check=False)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("gradle_lib", type=Path)
    parser.add_argument("--windows-archive", type=Path)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="manga-audit-") as temporary:
        if args.windows_archive:
            windows(args.windows_archive.resolve(), args.gradle_lib.resolve(), Path(temporary))
        else:
            linux(args.gradle_lib.resolve(), Path(temporary))
