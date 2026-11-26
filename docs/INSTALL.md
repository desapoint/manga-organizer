# Install Manga Organizer

Each pull request merged into `main` produces a GitHub development prerelease. Download the assets for one primary server and verify the file hashes against `SHA256SUMS.txt`. Windows and Linux packages are self-contained. The Windows setup program is unsigned. [GitHub Releases](https://github.com/desapoint/manga-organizer/releases)

## Database upgrades

The server applies pending SQLite migrations in order at startup and records them in `schema_migration`. Stop the server and back up its data directory before a maintenance upgrade. Run `MangaOrganizer.Server.exe --migrate` on Windows or `./MangaOrganizer.Server --migrate` on Linux to apply migrations without starting the server. Set `MANGA_ORGANIZER_DATA_DIR` to the same directory the server normally uses. A Docker container can run the same `--migrate` argument with its existing configuration volume mounted.

Migration SQL is embedded in the server and is available in the repository. The Windows and Linux migration jobs verify fresh databases, legacy upgrades and repeated runs. [Migration scripts](../src/MangaOrganizer.Server/Migrations) · [Verification workflow](../.github/workflows/migrations.yml)

When an existing database has pending migrations, startup and `--migrate` verify a copy of the SQLite database and server keys in `upgrade-backups` before applying changes. If backup verification fails, the upgrade stops. Keep the separate maintenance backup for media and recovery files. [Backup and restore rehearsal](UPGRADE-RECOVERY.md#packaged-startup-guard)

| Server | Release assets |
| --- | --- |
| Windows x64 | `MangaOrganizer-Setup-win-x64.exe` or `MangaOrganizer-win-x64.zip` |
| Linux x64 or ARM64 | `MangaOrganizer-linux-x64.tar.gz` or `MangaOrganizer-linux-arm64.tar.gz` |
| Docker amd64 or ARM64 | `MangaOrganizer-docker-install.zip` and a release image in GitHub Container Registry. Offline archives are `MangaOrganizer-docker-amd64.tar.gz` and `MangaOrganizer-docker-arm64.tar.gz`. |
| Unraid amd64 | `MangaOrganizer-unraid.xml` and the same Docker image |

Android, Windows and Linux clients use the server's WebUI. For these development builds, access the WebUI on the server loopback or through a tunnel. Keep the server's database and session keys in private local appdata.

## Windows x64

Run the setup program to install for your Windows user. Open **Manga Organizer** from the Start menu, or run `MangaOrganizer.Server.exe` from the extracted portable ZIP. On first launch the app opens a one-time link to the WebUI setup page. If the browser stays closed, open the link printed in the server window.

Choose and confirm an owner password of 12–256 characters. Use **Browse server** to choose an optional library folder, then finish setup and sign in. The server shows setup until an owner has been configured. On later launches it opens directly in server mode. The default data folder is `%LOCALAPPDATA%\MangaOrganizer`.

The WebUI defaults to `http://127.0.0.1:8787`. To choose a different local port, start the executable with `--urls http://127.0.0.1:8788` or set `ASPNETCORE_URLS` in its launching environment.

## Linux x64 or ARM64

Extract the archive for your architecture (`uname -m` reports `x86_64` or `aarch64`). Use a supported glibc-based Linux distribution with the standard .NET native prerequisites. The server includes the .NET runtime.

```bash
mkdir -p "$HOME/manga-organizer"
tar -xzf MangaOrganizer-linux-x64.tar.gz -C "$HOME/manga-organizer"
cd "$HOME/manga-organizer"
./MangaOrganizer.Server
```

Use the ARM64 archive filename in `tar` on ARM64. Open the one-time setup link printed in the terminal, then choose your owner password and optional library folder. The default data folder is `MangaOrganizer` under your user's local application data directory. For unattended or non-loopback first startup, set `MANGA_ORGANIZER_BOOTSTRAP_PASSWORD` in the launching environment, then clear it after the owner has been configured.

## Docker Compose

1. Extract `MangaOrganizer-docker-install.zip` and copy `.env.example` to `.env` in that directory. Set the absolute `MANGA_ORGANIZER_MEDIA_PATH` to an existing host folder and enter the first-run password. Keep `.env` private.
2. Run `docker compose up -d` and open `http://127.0.0.1:8787`. The default image reference is pinned to the release digest. If registry access is restricted, use the offline option below.
3. After the first successful sign-in, clear the password value in `.env` and run `docker compose up -d --force-recreate`. Keep the `manga-organizer-config` volume for subsequent updates. The media folder is mounted read-only.

To install offline, run `docker load -i MangaOrganizer-docker-amd64.tar.gz` or use the ARM64 archive. Replace the image line in `.env` with the commented offline image reference from `.env.example` and use `docker compose up -d --pull never`. Include `--pull never` whenever recreating the offline container. If the GHCR package is private, authenticate to `ghcr.io` with a package-reading account or use the offline archive. The package owner can enable anonymous pulls by making the package public.

## Unraid amd64

1. Save `MangaOrganizer-unraid.xml` as `my-Manga-Organizer.xml` in `/boot/config/plugins/dockerMan/templates-user/` on the server. Open **Docker → Add Container** and select **Manga-Organizer** under user templates.
2. Select a dedicated local appdata directory writable by UID and GID `1654`. For a new directory, an administrator can create it with `install -d -m 0700 -o 1654 -g 1654 /mnt/user/appdata/manga-organizer`. Pick an existing manga folder and enter the first-run password before applying the template.
3. The template binds port `8787` to the Unraid host's loopback address. For a remote browser, forward it with `ssh -N -L 8787:127.0.0.1:8787 <user>@<unraid-host>` and open `http://127.0.0.1:8787` on your client. Clear the first-run password from the template after sign-in and apply the change.

For an offline Unraid install, load `MangaOrganizer-docker-amd64.tar.gz` on the host and use the Compose bundle with its offline image reference where the Docker Compose plugin is installed. The Unraid template uses the registry image.

## Upgrades and catalog setup

Stop the running server before replacing binaries or changing the image. Back up its data directory or config volume and retain it across reinstalls. Open **Library settings** to add, edit or remove library and import folders. The server saves changes in SQLite. Existing first-launch paths and `MANGA_ORGANIZER_LIBRARY_ROOTS__0`, `MANGA_ORGANIZER_LIBRARY_ROOTS__1` and later values are used when seeding the database configuration. For Docker, bind-mount each folder before selecting it in the WebUI.

For automation, merges to `main` publish development prereleases with versioned images and SHA-256 checksums. Pull requests and manual runs validate the same build without publishing. Physical Unraid, release signing and full platform qualification continue in the audit ledger. [Qualification matrix](https://github.com/desapoint/manga-organizer/blob/main/docs/source-audit/DEPLOYMENT-QUALIFICATION.md)
