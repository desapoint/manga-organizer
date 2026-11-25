# Pass 21 — hosted Windows and Docker qualification

2026-11-25 Toronto. Both GitHub Actions jobs passed at `cb7c3b5`. The qualification workflow builds the synthetic fixture against the published Suwayomi `v2.4.2373` JAR. It uses a hosted Windows Server 2025 runner and an Ubuntu 24.04 Docker runner. [Passing workflow run](https://github.com/desapoint/manga-organizer/actions/runs/45664698558) · [Workflow](../../.github/workflows/audit-qualification.yml)

## Windows

The hosted job verified the portable Windows ZIP against its published SHA-256, ran its bundled JRE, installed the synthetic JAR, searched by a 64-bit source ID, downloaded chapter 12.5 and verified the PNG and `ComicInfo.xml`. The same job passed case-only rename, same-volume hardlink identity, exact-copy distinction and exclusive no-clobber checks. It also observed the populated-queue start timeout after the chapter was downloaded. Windows client installation and upgrades, SMB/UNC, drive removal and cross-volume journal recovery remain local tests. [Passing Windows job](https://github.com/desapoint/manga-organizer/actions/runs/45664698558/job/139383566385)

## Docker configuration

The Docker fixture uses the pinned amd64 engine at explicit UID:GID `99:100`, separate writable configuration and media volumes, a read-only synthetic provider bind, and a loopback-published API port. It verifies the downloaded bytes and metadata on the media volume, then restarts the engine and checks the installed extension and files again. The release's custom `downloadsPath` requires a directory that already exists. With the target absent, the earlier fixture downloaded into the engine's default directory, so its intended media-volume check failed. The fixture now creates the destination before launch and checks the output there.

The corrected runs passed on the local Debian 13 rootless Docker host and on GitHub Actions. The engine downloaded chapter 12.5 at UID:GID `99:100`, and the harness verified the single matching PNG and `ComicInfo.xml` before and after container restart. The populated-queue start returned a GraphQL timeout after the chapter was downloaded, consistent with Pass 20. [Passing Ubuntu Docker job](https://github.com/desapoint/manga-organizer/actions/runs/45664698558/job/139383565676)

To replay on Linux amd64, supply Java 21 or newer and the `lib` directory from the verified Gradle 9.9.2 distribution. The script creates its own disposable volumes and bridge network. It publishes only the local test API port.

```sh
python3 docs/source-audit/fixtures/suwayomi/run_qualification.py /path/to/gradle-9.9.2/lib
```

The local host checks cover Unraid pool and `/mnt/user` behavior, SMB/NFS, exceptional mount and permission transitions, and the Windows installer. Android/Mihon interoperability and APK or store cases require separate runs. The audit's remaining source, rights and owner approval gates are in the [completion ledger](COMPLETION-LEDGER.md).
