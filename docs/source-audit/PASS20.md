# Pass 20 — Linux qualification, extension download and artifact verification

2026-11-25 Toronto. All source tests used the seven revisions in the [source registry](README.md). Runtime probes used Debian 13 Linux amd64 and Docker Engine 29.9.2 in rootless mode. Test media and application state were synthetic. The [completion ledger](COMPLETION-LEDGER.md) records the evidence still required for audit sign-off.

## Published Suwayomi v2.4.2373

The Linux x64 archive matched the published SHA-256 `29de723470f2e89dc1f0730e7eeb69c6a048a83217b1b2fd8451441d267e2ec2`. The bundled JRE reports `25.0.5.1` and the Electron version file reports `37.2.5`. The standalone server and the official amd64 image both contained server JAR SHA-256 `dd05d523cc785b7acf90c9feac0bc84c53d5fb3af5ab2a33653646e2076e5217`. The Docker amd64 manifest is `sha256:f87c945e76240adcb305f75c6fdf111d3917cd11834bdb1db5e95796778d4260`. The image declared user `suwayomi` and its version label matched the authenticated API. The separately reviewed Dockerfile at `a44c088ba9c80879fb23c2822cb28ace33f153e1` uses a mutable Temurin image tag and a release URL for the JAR. The image's `tachidesk.docker_commit` label was `489`, which does not establish an exact source revision for the tested image. [Published release](https://github.com/Suwayomi/Suwayomi-Server/releases/tag/v2.4.2373) · [Companion Dockerfile](https://github.com/Suwayomi/Suwayomi-Server-docker/blob/a44c088ba9c80879fb23c2822cb28ace33f153e1/Dockerfile)

| Check | Observed result |
|---|---|
| Version, auth and introspection | The authenticated `POST /api/graphql` reported `v2.4.2373` / `r2373`. Introspection returned 32 query fields and 80 mutation fields. Sources expose `LongString` IDs. The extension type includes `apkUrl`, `jarUrl`, `storeIndexUrl` and `versionCodeLong`. The unauthenticated sources request returned HTTP 404. Basic-auth multipart upload without a session failed with 401. The same request with the server-issued session and CSRF cookies succeeded. |
| Synthetic JAR install and download | Installed library version `1.4` JAR `1.4.1` via `installExternalExtension`. Search returned source ID `7200000000000000123` as a string. Listed and enqueued `Chapter 12.5`. During the start call the queue went from STOPPED through STARTED back to STOPPED, and `chapter` reported one downloaded page. The engine wrote a loose PNG and `ComicInfo.xml`. Page SHA-256 `0a98a1953a4db54b56759b5b091b0a1f0f6741817d2ca256412cddd181eff98b` matched the page served by the fixture. |
| Start acknowledgement | With an empty queue, the standalone server logged a 30-second timeout. The container returned HTTP 200 with `startDownloader: null`, a GraphQL timeout error, and subsequent `STOPPED` with an empty queue. The standalone start call with a populated queue returned the same GraphQL timeout even though the chapter and file were already present. Use both GraphQL `errors` and reconciled engine/file state before deciding whether a request succeeded. |
| Version compatibility | Replaced `1.4.1` with the higher-code `1.4.2` via explicit external upload. The incompatible library version `1.7` JAR produced a GraphQL error and left `1.4.2` installed. This exercised externally uploaded JARs with the same package identity. |
| Restart and uninstall | After a graceful shutdown and restart, `1.4.2`, the downloaded flag and the exact PNG bytes persisted. Uninstall removed the source and the installed package from their list queries. The downloaded page remained on disk. A singleton lookup of the removed package produced a GraphQL non-null field error. |
| Backup export and restore | Exported a gzip backup containing one synthetic library item and its chapter. Validated it and restored into a separate amd64 container running the same engine version with the fixture extension installed. Both missing-entity lists were empty and restore reached `SUCCESS`. A fresh query confirmed the library flag, selected source ID and chapter 12.5. The restored chapter reported `isDownloaded: false`. |

The release start mutation waits on a `Started` update after calling `start()`. The pinned branch returns current status immediately for an empty queue. Keep the release outcome and branch evidence attached to their respective revisions. For the populated-queue timeout, the passing file and status checks establish that an error response can follow successful work. The backup result applies to the same-version engine; the Android/Mihon and legacy-format runs require their own fixtures. [Release start implementation](https://github.com/Suwayomi/Suwayomi-Server/blob/9f8cc818b5df6d0e7e10e0de415903635d2c04de/server/src/main/kotlin/suwayomi/tachidesk/graphql/mutations/DownloadMutation.kt#L213-L238) · [Pinned source evidence](PASS20-EVIDENCE.csv)

### Synthetic fixture

Use a disposable engine and an isolated provider endpoint. The fixture serves a generated 2×2 PNG, one title and chapter 12.5. It provides JARs `1.4.1`, `1.4.2` and incompatible `1.7.1`. To build and serve it from the repository root, use the verified Suwayomi JAR, the `lib` directory of Gradle 9.9.2, and a new scratch directory. The Java system property `manga.audit.fixtureUrl` sets the fixture endpoint when the engine runs in a separate network namespace. [Fixture source](fixtures/suwayomi/SyntheticSource.kt) · [Fixture builder](fixtures/suwayomi/make_fixture.py)

```sh
python3 docs/source-audit/fixtures/suwayomi/make_fixture.py /path/to/Suwayomi-Server.jar /path/to/gradle-9.9.2/lib /tmp/manga-audit-fixture
python3 -m http.server 14569 --bind 127.0.0.1 --directory /tmp/manga-audit-fixture/site
```

Use `aboutServer` to establish the basic-auth session before multipart operations. Exercise `installExternalExtension`, `fetchSourceManga`, `fetchMangaAndChapters`, `enqueueChapterDownload`, then `startDownloader` with a populated queue. Poll `chapter` and `downloadStatus`, compare the SHA-256 of the downloaded page, and run `createBackup`, `validateBackup` and `restoreBackup` against a second isolated instance. Retain HTTP status, GraphQL errors and the durable contents for each result.

## Upstream and filesystem checks

| Reference | Linux execution | Outcome |
|---|---|---|
| KR | Python 3.12.14 targeted backend tests across locking, scanning, rename, publish, missing roots, importer, qBittorrent, settings and collisions. Node 24.22.3 frontend locked installation and build. | 144 backend tests and 11 frontend tests passed. Frontend built. Separately, the two hashed backend requirement files installed, the wheel built without dependency resolution or build isolation, and dependency check passed. |
| KK | Python 3.12.14 targeted tests across backup, ownership, queue, publication journal, hardlink copy-on-write, file observation, Suwayomi and deletion journal. | 392 passed, one setup error. The foreign-UID test fixture expects one literal Python `FROM` and the pinned Dockerfile is multi-stage. Its cross-UID assertion never ran. |
| UC | Node 24.22.3, TypeScript build, PostgreSQL 16.16 container. Selected archive, backup store, bare-mount cleanup, edition, extension, fingerprint, numbering, progress and rescan tests. | Build passed. All 198 selected tests passed with the database. Six additional backup tests passed against a separate database with actual `pg_dump` 16.16. An earlier broader glob run had one unrelated solver timing assertion fail and 121 DB-dependent skips before PostgreSQL was configured. |
| MK | .NET SDK 10.0.404 in Linux Docker. Filters `ChapterFileDuplicateRepairTests`, `ChapterDownloadProcessorRenameRaceTests`, `DownloadQueueRecoveryTests`, `BackupRestoreTests`. Separate Node 24.22.3 frontend build. | 38 passed. The separate locked frontend install and production build passed. |
| KV | .NET SDK 10.0.404 in Linux Docker. Filters `MountDetectorServiceTests`, `FolderValidationServiceTests`, `ScannerServiceScanFolderDedupeTests`, `ReaderServiceRereadTests`, `BackupServiceTests`. | 72 passed. NuGet restore also reported advisories for resolved AutoMapper and other dependencies that remain part of release review. |
| SW | Pinned branch Gradle 9.9.2 and verified Temurin 21 JDK. Selected download, extension and backup test filters. | Build stopped before the tests. The configured artifact repositories returned HTTP 403 while resolving the pinned Aliucord `axml` and JetBrains JCEF dependencies. The published-release runtime checks above ran separately. |
| MH | Pinned Mihon app `BackupStreamTest`, Gradle 9.9.2 and Temurin 21 JDK. | Android Gradle configuration ran after redirecting its state to the workspace. The test task stopped before execution because the Android SDK was absent. |
| Docker VFS | Nonroot Python process, read-only container root, disposable tmpfs volumes. | Same-mount hardlinks shared device and inode. Byte-identical copies used distinct inodes. Exclusive publication kept an occupied destination's bytes, and cross-filesystem hardlink returned `EXDEV`. These are Linux tmpfs results. |

The Uchiyomi database fixture ran against image `postgres@sha256:f2f98726820d0342055351c6e9d36e705f5bfffb3dd53ff556325114b08afb6d`. Its dump wrapper routed the fixture connection to `pg_dump` in the same container. The application source tests exercised selected mount disappearance, renumber crash/journal recovery, progress/order and requester permissions. The Manga-arr setup finding is traceable to the pinned actor fixture and Dockerfile. [Source evidence](PASS20-EVIDENCE.csv)

## Artifact and licensing checks

- The pinned Manga-arr 7-Zip installer verified both official 26.06 Linux archives and their embedded machine IDs before staging. The x64 binary reported 26.06 when executed. The arm64 binary was checked as an artifact. The extracted notice identifies LGPL terms, BSD-licensed components and the unRAR restriction. Chosen packaging still needs exact notices and distribution review. [Upstream 7-Zip release](https://github.com/ip7z/7zip/releases/tag/26.06)
- The verified Suwayomi Linux archive includes Java module license files, Electron's license and bundled Chromium notices. The upstream checksum file lists the Windows MSI and portable ZIP digests. Windows installer execution, signing and a distribution SBOM remain open. [Published checksums](https://github.com/Suwayomi/Suwayomi-Server/releases/download/v2.4.2373/Checksums.sha256)

## Coverage and open gates

The 22-path Uchiyomi release diff includes a new blocked-group purge absent from `v0.109.0`. The pinned branch locks and revalidates previously selected download rows, then delegates to a helper that removes media before deleting progress, bookmarks and offline state. This does not supply crash-safe recovery or authority to count a source-policy exclusion as a duplicate. Keep the result scoped to the branch. [Release comparison](https://github.com/AngeloSha/uchiyomi/compare/v0.109.0...dc5d52212cbe7ad1984fbb29d1490e524f886e0c) · [Source evidence](PASS20-EVIDENCE.csv)

Pass 20 adds nine distinct mapped paths, for 443 cited paths across 13,716 indexed paths. Of 7,716 implementation candidates, 344 have mapped citations and 7,372 remain without mapped citations. The change in counts measures evidence coverage. Full module dispositions can be regenerated with `python3 docs/source-audit/reconcile-coverage-through17.py --through-pass 20`.

| Required outcome | Evidence still needed |
|---|---|
| F12 Mihon-compatible extension downloads | Repeat the supported JAR workflow on Windows and Unraid, test approved APK and source-store cases, check Mihon backup compatibility, and qualify the staged output and admission contract for organizer import. |
| F57 Windows | Qualify the x64 installer and shutdown/upgrade lifecycle, NTFS and UNC, case-only rename, locks, and cross-volume recovery. |
| F58 Unraid and Docker WebUI | Qualify the actual Unraid template, numeric ownership, pool/share and SMB/NFS behavior, engine lifecycle and backup/restore. |
| F69 Physical organization | Exercise preview/revalidation, no-clobber publication and mutation journal recovery on each target storage class. |
| F70 Duplicate handling | Verify alias vs exact copy, repacks and overlapping editions, preserve unique chapters and reader state, and measure reclaimed blocks after a reviewed operation. |

Broad module review, exact release licensing and owner decisions remain open. [Completion ledger](COMPLETION-LEDGER.md)
