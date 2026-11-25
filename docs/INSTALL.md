# Install Manga Organizer

Each pull request merged into `main` produces a GitHub development prerelease. Download the assets for one primary server and verify the file hashes against `SHA256SUMS.txt`. Windows and Linux packages are self-contained. The Windows setup program is unsigned. [GitHub Releases](https://github.com/desapoint/manga-organizer/releases)

| Server | Release assets |
| --- | --- |
| Windows x64 | `MangaOrganizer-Setup-win-x64.exe` or `MangaOrganizer-win-x64.zip` |
| Linux x64 or ARM64 | `MangaOrganizer-linux-x64.tar.gz` or `MangaOrganizer-linux-arm64.tar.gz` |
| Docker amd64 or ARM64 | `MangaOrganizer-docker-install.zip` and a release image in GitHub Container Registry. Offline archives are `MangaOrganizer-docker-amd64.tar.gz` and `MangaOrganizer-docker-arm64.tar.gz`. |
| Unraid amd64 | `MangaOrganizer-unraid.xml` and the same Docker image |

Android, Windows and Linux clients use the server's WebUI. For these development builds, access the WebUI on the server loopback or through a tunnel. Keep the server's database and session keys in private local appdata.

## Windows x64

Run the setup program to install for your Windows user and add a Start menu shortcut. Alternatively extract the portable ZIP into its own folder. Choose an owner password of 12–256 characters. Set it only for the first launch from PowerShell.

```powershell
$secret = Read-Host 'Choose the owner password' -AsSecureString
$env:MANGA_ORGANIZER_BOOTSTRAP_PASSWORD = [System.Net.NetworkCredential]::new('', $secret).Password
try {
    & "$env:LOCALAPPDATA\Programs\Manga Organizer\MangaOrganizer.Server.exe"
} finally {
    Remove-Item Env:MANGA_ORGANIZER_BOOTSTRAP_PASSWORD -ErrorAction SilentlyContinue
}
```

For the portable package, run `./MangaOrganizer.Server.exe` from the extracted folder in the `try` block. Open `http://127.0.0.1:8787` and sign in. On subsequent runs, open **Manga Organizer** from the Start menu or run the executable. The default data folder is `%LOCALAPPDATA%\MangaOrganizer`. To add a catalog folder, set `MANGA_ORGANIZER_LIBRARY_ROOTS__0` in the launching environment to its absolute Windows path.

## Linux x64 or ARM64

Extract the archive for your architecture (`uname -m` reports `x86_64` or `aarch64`). Use a supported glibc-based Linux distribution with the standard .NET native prerequisites. The server includes the .NET runtime.

```bash
mkdir -p "$HOME/manga-organizer"
tar -xzf MangaOrganizer-linux-x64.tar.gz -C "$HOME/manga-organizer"
cd "$HOME/manga-organizer"
read -rsp 'Choose the owner password: ' owner_password
printf '\n'
MANGA_ORGANIZER_BOOTSTRAP_PASSWORD="$owner_password" ./MangaOrganizer.Server
unset owner_password
```

Use the ARM64 archive filename in `tar` on ARM64. Open `http://127.0.0.1:8787` and sign in. Subsequent launches use `./MangaOrganizer.Server`. Set `MANGA_ORGANIZER_LIBRARY_ROOTS__0` to an absolute local catalog folder in the launching environment. The default data folder is `MangaOrganizer` under your user's local application data directory.

## Docker Compose

1. Extract `MangaOrganizer-docker-install.zip` and copy `.env.example` to `.env` in that directory. Set the absolute `MANGA_ORGANIZER_MEDIA_PATH` to an existing host folder and enter the first-run password. Keep `.env` private.
2. Run `docker compose up -d` and open `http://127.0.0.1:8787`. The default image reference is pinned to the release digest. If registry access is restricted, use the offline option below.
3. After the first successful sign-in, clear the password value in `.env` and run `docker compose up -d --force-recreate`. Keep the Compose `config` volume for subsequent updates. The media folder is mounted read-only.

To install offline, run `docker load -i MangaOrganizer-docker-amd64.tar.gz` or use the ARM64 archive. Replace the image line in `.env` with the commented offline image reference from `.env.example` and use `docker compose up -d --pull never`. Include `--pull never` whenever recreating the offline container. If the GHCR package is private, authenticate to `ghcr.io` with a package-reading account or use the offline archive. The package owner can enable anonymous pulls by making the package public.

## Unraid amd64

1. Save `MangaOrganizer-unraid.xml` as `my-Manga-Organizer.xml` in `/boot/config/plugins/dockerMan/templates-user/` on the server. Open **Docker → Add Container** and select **Manga-Organizer** under user templates.
2. Select a dedicated local appdata directory writable by UID and GID `1654`. For a new directory, an administrator can create it with `install -d -m 0700 -o 1654 -g 1654 /mnt/user/appdata/manga-organizer`. Pick an existing manga folder and enter the first-run password before applying the template.
3. The template binds port `8787` to the Unraid host's loopback address. For a remote browser, forward it with `ssh -N -L 8787:127.0.0.1:8787 <user>@<unraid-host>` and open `http://127.0.0.1:8787` on your client. Clear the first-run password from the template after sign-in and apply the change.

For an offline Unraid install, load `MangaOrganizer-docker-amd64.tar.gz` on the host and use the Compose bundle with its offline image reference where the Docker Compose plugin is installed. The Unraid template uses the registry image.

## Upgrades and catalog setup

Stop the running server before replacing binaries or changing the image. Back up its data directory or config volume and retain it across reinstalls. Additional server folders can be configured with `MANGA_ORGANIZER_LIBRARY_ROOTS__1`, `MANGA_ORGANIZER_LIBRARY_ROOTS__2` and so on. Restart and select **Scan folder** in the WebUI.

For automation, merges to `main` publish development prereleases with versioned images and SHA-256 checksums. Pull requests and manual runs validate the same build without publishing. Physical Unraid, release signing and full platform qualification continue in the audit ledger. [Qualification matrix](https://github.com/desapoint/manga-organizer/blob/main/docs/source-audit/DEPLOYMENT-QUALIFICATION.md)
