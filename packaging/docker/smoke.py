"""Launch the generated Docker Compose release bundle and check the Unraid template."""

import os
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
from uuid import uuid4
from xml.etree import ElementTree
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tests"))
from foundation_smoke import Browser, wait_for_server


artifacts = Path(sys.argv[1])
image = sys.argv[2]
template = ElementTree.parse(artifacts / "MangaOrganizer-unraid.xml").getroot()
assert template.findtext("Repository") == image
assert template.findtext("Privileged") == "false"
assert template.findtext("ExtraParams") == "--publish 127.0.0.1:8787:8787"
assert template.find("./Config[@Target='/media/manga']").get("Mode") == "ro"
assert template.find("./Config[@Target='/config']").get("Mode") == "rw"

with TemporaryDirectory() as temporary:
    path = Path(temporary)
    with ZipFile(artifacts / "MangaOrganizer-docker-install.zip") as bundle:
        bundle.extractall(path)
    assert f"MANGA_ORGANIZER_IMAGE={image}" in (path / ".env.example").read_text(encoding="utf-8")
    media = path / "media"
    media.mkdir(mode=0o755)
    (media / "Chapter 01.cbz").write_bytes(b"Synthetic catalog entry")
    password = uuid4().hex
    project_name = f"manga-release-smoke-{uuid4().hex}"
    env = dict(os.environ, MANGA_ORGANIZER_IMAGE=image, MANGA_ORGANIZER_MEDIA_PATH=str(media),
               MANGA_ORGANIZER_CONFIG_VOLUME=f"{project_name}-config",
               MANGA_ORGANIZER_BOOTSTRAP_PASSWORD=password, MANGA_ORGANIZER_PORT="0")
    compose = ["docker", "compose", "--project-name", project_name]
    subprocess.run([*compose, "config", "--quiet"], cwd=path, env=env, check=True)
    try:
        subprocess.run([*compose, "up", "--detach", "--pull", "never"], cwd=path, env=env, check=True)
        address = subprocess.check_output([*compose, "port", "manga-organizer", "8787"], cwd=path, env=env, text=True).strip()
        assert address.startswith("127.0.0.1:")
        wait_for_server(f"http://{address}")
        browser = Browser(f"http://{address}")
        browser.sign_in(password)
        (root,) = browser.roots()
        assert browser.scan(root["id"])["fileCount"] == 1
        assert browser.files(root["id"])["items"][0]["relativePath"] == "Chapter 01.cbz"
    except Exception:
        subprocess.run([*compose, "logs"], cwd=path, env=env, check=False)
        raise
    finally:
        subprocess.run([*compose, "down", "--volumes"], cwd=path, env=env, check=True)
