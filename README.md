# Manga Organizer

A documentation-first project for a unified, self-hosted manga library, acquisition manager and reader on Windows and Unraid/Docker.

**Status** — primary-server foundation and read-only catalog in development. The audit ledger tracks remaining source, license and device qualification. [Audit ledger](docs/source-audit/COMPLETION-LEDGER.md)

Install the Windows, Linux, Docker or Unraid build from a GitHub development prerelease. A merged pull request to `main` builds and publishes the installers and container image after the package tests pass. [Releases](https://github.com/desapoint/manga-organizer/releases) · [Installation guide](docs/INSTALL.md)

## Run the primary-server foundation

Build the WebUI with Node.js 24 and publish the server with the .NET 10 SDK.

```sh
npm --prefix client ci
npm --prefix client run build
dotnet restore src/MangaOrganizer.Server --locked-mode
dotnet publish src/MangaOrganizer.Server --no-restore -c Release -o ./publish
```

Set `MANGA_ORGANIZER_BOOTSTRAP_PASSWORD` in the launching process on first startup. Choose 12–256 characters. Run `./publish/MangaOrganizer.Server.exe` on Windows or `./publish/MangaOrganizer.Server` on Linux, then open [the local WebUI](http://127.0.0.1:8787). The server saves a password hash and its ID in the current user's local appdata. Set `MANGA_ORGANIZER_DATA_DIR` to use a different local directory. Remove the bootstrap variable after the first start.

The same build runs in Docker with a named volume for the database and session keys. Set the bootstrap variable in your shell before the first run.

```sh
docker build -t manga-organizer:dev .
docker run --rm --name manga-organizer -p 127.0.0.1:8787:8787 \
  --mount type=volume,source=manga-organizer-config,target=/config \
  --env MANGA_ORGANIZER_BOOTSTRAP_PASSWORD manga-organizer:dev
```

On subsequent starts, omit the bootstrap `--env` option and reuse the volume. Keep the listener on loopback while remote deployment is being qualified. Keep the local appdata directory private because it includes the session keys.

### Configure the read-only library

Set `MANGA_ORGANIZER_LIBRARY_ROOTS__0` to an absolute path on the server. Use `MANGA_ORGANIZER_LIBRARY_ROOTS__1`, `MANGA_ORGANIZER_LIBRARY_ROOTS__2` and so on for additional folders. Keep these folders separate from each other and from the server data directory. Restart, sign in and select **Scan folder** to populate the catalog.

In Docker, use a read-only bind mount and set the environment variable to its container path. Add these options before the image name in `docker run`.

```sh
--mount type=bind,source=/path/to/manga,target=/media/manga,readonly \
--env MANGA_ORGANIZER_LIBRARY_ROOTS__0=/media/manga
```

The catalog records paths, sizes and timestamps in SQLite and keeps the last successful inventory when a root goes offline. See the [read-only catalog scope](docs/FILE-ORGANIZATION.md#read-only-catalog).

## Start here

- [Reference repositories (all seven)](REFERENCES.md)
- [Per-project documentation audit and comparison](docs/UPSTREAM-AUDIT.md)
- [Pinned source-code audit and observations](docs/source-audit/README.md)
- [Granular candidate features](docs/source-audit/ATOMIC-FEATURES.csv)
- [Feature selection matrix](docs/FEATURE-MATRIX.md)
- [Editable parent selection CSV](docs/FEATURE-SELECTION.csv)
- [Mihon/Suwayomi extension integration](docs/EXTENSIONS.md)
- [File organization and duplicate management (required)](docs/FILE-ORGANIZATION.md)
- [Remote reading and device sync](docs/REMOTE-READING.md)
- [Proposed architecture and deployment](docs/ARCHITECTURE.md)
- [Approved implementation baseline](docs/IMPLEMENTATION-DECISIONS.md)
- [Roadmap and acceptance gates](docs/ROADMAP.md)
- [Contributing and audit protocol](CONTRIBUTING.md)

The project targets a main library, one queue and a shared WebUI. One Windows or Linux server hosts the main library and validates reading progress for Android, Windows and Linux clients. The project also requires Mihon-compatible extension downloads, physical file organization and duplicate handling. Clients may optionally upload local files or subscribe to additional server libraries. [Audit completion ledger](docs/source-audit/COMPLETION-LEDGER.md)
