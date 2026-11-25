"""Exercise the published server through its browser-facing HTTP API."""

import argparse
from contextlib import contextmanager
from http.cookiejar import CookieJar
from pathlib import Path
import json
import os
import re
import socket
import subprocess
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.request import HTTPCookieProcessor, Request, build_opener
from uuid import UUID, uuid4


BOOTSTRAP_VARIABLE = "MANGA_ORGANIZER_BOOTSTRAP_PASSWORD"


class Browser:
    def __init__(self, url, cookies=None):
        self.url = url
        self.cookies = CookieJar() if cookies is None else cookies
        self.opener = build_opener(HTTPCookieProcessor(self.cookies))
        self.headers = None

    def request(self, path, expected, *, method="GET", body=None, token=None):
        headers = {}
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


@contextmanager
def start_app(executable, data_dir, password):
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        url = f"http://127.0.0.1:{listener.getsockname()[1]}"
    env = dict(os.environ, ASPNETCORE_URLS=url, MANGA_ORGANIZER_DATA_DIR=str(data_dir))
    env.pop(BOOTSTRAP_VARIABLE, None)
    if password is not None:
        env[BOOTSTRAP_VARIABLE] = password
    with tempfile.TemporaryFile() as log, subprocess.Popen([executable], env=env, stdout=log, stderr=log) as process:
        try:
            yield url
        except Exception:
            log.seek(0)
            print(log.read().decode(errors="replace"))
            raise
        finally:
            process.terminate()
            process.wait(timeout=15)


@contextmanager
def start_container(image, volume, password):
    name = f"manga-smoke-{uuid4().hex}"
    command = ["docker", "run", "--detach", "--name", name, "-p", "127.0.0.1::8787",
               "--mount", f"type=volume,source={volume},target=/config"]
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
    for _ in range(60):
        try:
            Browser(url).request("/healthz", 200)
            return
        except URLError:
            time.sleep(0.25)
    raise RuntimeError("Server did not start")


def smoke(start):
    password = uuid4().hex
    with start(password) as url:
        wait_for_server(url)
        first, second = Browser(url), Browser(url)
        html = first.request("/", 200).decode()
        assert "<title>Manga Organizer</title>" in html
        (script,) = re.findall(r'<script[^>]+src="([^"]+)"', html)
        assert first.request(script, 200)
        first.request("/api/server", 401)
        first.request("/api/session", 400, method="POST", body={"password": password})
        first.sign_in(uuid4().hex, 401)
        first.sign_in(password)
        (session,) = first.headers.get_all("Set-Cookie")
        assert session.startswith("MangaOrganizer.Session.") and "; httponly" in session.lower()
        server_id = first.server_id()
        second.request("/api/server", 401)
        second.sign_in(password)
        assert second.server_id() == server_id
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


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--app", type=Path, help="published executable")
    mode.add_argument("--image", help="built Docker image")
    target = parser.parse_args()
    if target.app:
        with tempfile.TemporaryDirectory() as data_dir:
            smoke(lambda password: start_app(str(target.app.resolve()), data_dir, password))
    else:
        volume = f"manga-smoke-{uuid4().hex}"
        subprocess.run(["docker", "volume", "create", volume], check=True, stdout=subprocess.DEVNULL)
        try:
            smoke(lambda password: start_container(target.image, volume, password))
        finally:
            subprocess.run(["docker", "volume", "rm", volume], check=True, stdout=subprocess.DEVNULL)
    print("Passed WebUI assets, authentication, CSRF, two clients, and restart persistence")
