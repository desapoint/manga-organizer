# Pass 7 — licenses, dependencies, models and deployment

Date: 2026-11-25. Partial static audit; 54 distinct pinned files reviewed including seven top-level license texts and a nested Apache notice. Selected manifest/build/source extracts, not a resolved SBOM or executed builds. All optional choices pending.

[Evidence](PASS7-EVIDENCE.csv) · [License/asset register](LICENSE-ASSET-REGISTER.csv) · [Deployment qualification](DEPLOYMENT-QUALIFICATION.md)

## KR — KiranTheRam/mangarr

- [LICENSE](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/LICENSE#L73) (C, 73-621): GPL v3 text; distribution source obligations sections 4-6. **Implication:** No code reuse until license choice and scope review.
- [backend/pyproject.toml](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/pyproject.toml#L3) (C, 3-40): Python project version and direct dependencies. **Implication:** Resolve lock and exact license graph.
- [backend/requirements.txt](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/requirements.txt#L7) (C, 7-934): Pinned hashed dependency list includes FastAPI, SQLAlchemy, aiosqlite, Pillow, lxml, mini-racer. **Implication:** Generate artifact SBOM and verify actual resolved dependencies.
- [docker-compose.yml](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/docker-compose.yml#L3) (C, 3-20): Config and common media mount; optional numeric UID/GID; restart. **Implication:** One common data mount supports hardlinks where filesystem permits.
- [docker/Dockerfile](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/docker/Dockerfile#L2) (C, 2-54): Digest-pinned Node 22 and Python 3.12, npm ci, config volume, 6996, healthcheck follows port. **Implication:** Pin base image, expose versioned health endpoints.
- [docker/entrypoint.sh](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/docker/entrypoint.sh#L17) (L, 17-61): Both UID/GID or neither, numeric validation, config ownership correction, setpriv. **Implication:** Never recursively chown media; default non-root required.

## MK — OrbitMPGH/Maki

- [LICENSE](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/LICENSE#L73) (C, 73-621): GPL v3 text. **Implication:** Same.
- [Directory.Build.props](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/Directory.Build.props#L4) (C, 4-36): Version and commit passed from CI; host RID selection in nonrelease builds. **Implication:** Repeatable versioned build identity.
- [src/Maki.Api/Maki.Api.csproj](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Maki.Api.csproj#L25) (C, 25-59): net10.0, EF/OIDC/SignalR/Quartz, OCR and upscaler notice files copied, libjxl target. **Implication:** Audit native and managed dependencies plus license notices.
- [src/Maki.Desktop/Maki.Desktop.csproj](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Desktop/Maki.Desktop.csproj#L4) (C, 4-34): Windows Forms tray app, child API process, Velopack. **Implication:** Native desktop wrapper candidate, not chosen stack.
- [distribution/docker/Dockerfile](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/distribution/docker/Dockerfile#L8) (C, 8-62;175-252): Node 24, .NET 10, browser deps and linkage check, native JXL, config, 8990, tini, healthcheck; disables W^X mitigation for memory tradeoff. **Implication:** Do not copy mitigation disable; benchmark with secure defaults.
- [distribution/docker/entrypoint.sh](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/distribution/docker/entrypoint.sh#L4) (L, 4-16): UID/GID 1000 defaults; only mismatched config ownership changed; gosu. **Implication:** Restrict ownership edits to owned appdata and explicit admin consent.
- [distribution/native/windows-setup.iss](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/distribution/native/windows-setup.iss#L22) (C, 22-46;139-154): Inno Setup wrapper, per-user lowest privilege, Win10 minimum declared, configurable location. **Implication:** Separate binary install and user data.
- [site/src/content/docs/installation/unraid.md](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/site/src/content/docs/installation/unraid.md#L13) (C, 13-25): 8990, /config, /library, Unraid example 99:100. **Implication:** Example, not proof of target qualification.
- [THIRD_PARTY_NOTICES.md](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/THIRD_PARTY_NOTICES.md#L3) (C, 3-71;204-317): Maki models GPL, inherited BSD weights, SPAN-S noncommercial, Real-ESRGAN BSD, OCR Apache, native JXL and browser/shader notices. **Implication:** Separate every artifact license and provenance; exclude NC by default.
- [src/Maki.Api/Services/Upscaling/UpscaleModelCatalog.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Services/Upscaling/UpscaleModelCatalog.cs#L18) (C, 18-89): Four model IDs, model adapter metadata, license and source labels. **Implication:** Model catalog must include rights and checksums.
- [src/Maki.Api/Services/Upscaling/UpscaleModelStore.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Services/Upscaling/UpscaleModelStore.cs#L19) (C, 19-120): Model manifest SHA256 field, bundled/downloaded state, directory and status. **Implication:** Verify complete signature/hash and atomic download path later.
- [distribution/native/build-windows.ps1](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/distribution/native/build-windows.ps1#L3) (C, 3-43;59-144): Self-contained win-x64 API and tray, portable/update feed, Inno wrapper, notices, temporary config smoke path; unsigned documented. **Implication:** Sign installer and updates before public release; test actual install/upgrade.
- [site/src/content/docs/installation/unraid-paths.md](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/site/src/content/docs/installation/unraid-paths.md#L12) (C, 12-35): Appdata contains secrets/db/cache/backups; stop before copying; stable container paths. **Implication:** Use consistent snapshot backups, local pool.

## UC — AngeloSha/uchiyomi

- [LICENSE](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/LICENSE#L4) (C, 4-232): MPL 2.0 file-level covered source/executable and larger work terms. **Implication:** Check file headers and secondary license eligibility.
- [desktop/electron-builder.yml](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/desktop/electron-builder.yml#L1) (C, 1-62): Windows x64 per-user NSIS, unsigned, updater metadata, keep appdata on uninstall; macOS dmg/zip. **Implication:** Sign releases; preserve user data.
- [desktop/package.json](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/desktop/package.json#L4) (C, 4-34): Same server and web app, Electron 45.3.1, updater 6.8.11, pack and test scripts. **Implication:** Separate standalone and remote modes; verify native deps.
- [deploy/docker-compose.yml](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/deploy/docker-compose.yml#L7) (C, 7-84): Embedded PostgreSQL or external DATABASE_URL, app/library/download/config volumes, optional UID/GID, 8080:3000, SIGTERM grace comment. **Implication:** DB local; keep media separate from config; test graceful shutdown.
- [Dockerfile.aio](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/Dockerfile.aio#L25) (C, 25-58): Node 24 Alpine build stages, npm ci fallback npm install, embedded PG support and su-exec. **Implication:** Do not allow unlocked fallback in release CI.
- [services/title-extractor/NOTICE](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/services/title-extractor/NOTICE#L3) (C, 3-15): MPL service, pinned Apache Hi-SAM, Segment Anything notices, checkpoints excluded. **Implication:** Code license not model rights.
- [services/title-extractor/requirements.txt](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/services/title-extractor/requirements.txt#L1) (C, 1-5): Optional worker dependency manifest retrieved; selected extraction returned no matching dependency lines. **Implication:** Inspect exact package versions and rights separately.
- [services/title-extractor/README.md](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/services/title-extractor/README.md#L33) (C, 33-95): Separate CPU image amd64/arm64, no weights, OCR or media included, operator-staged assets with hashes; desktop does not auto-start worker. **Implication:** Optional isolated sidecar; no auto-fetch unapproved assets.
- [bff/docker-entrypoint.sh](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/docker-entrypoint.sh#L27) (L, 27-62): Default UID/GID 10002, reject root app, numeric su-exec; only app-owned volume ownership, never user library. **Implication:** Adopt explicit read-only default and no blanket chown.
- [bff/package.json](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/package.json#L6) (C, 6-38): Node >=24, Fastify, MuPDF 1.28.6 range, pg, sharp, ZIP/RAR, argon2, web push. **Implication:** MuPDF AGPL/commercial license blocker; verify resolved version.

## KK — Kha-kis/manga-arr

- [LICENSE](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/LICENSE#L186) (C, 186-214): AGPL v3 section 13 remote network interaction; container label says only. **Implication:** Modified network-served covered code requires source offer as applicable.
- [Dockerfile](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/Dockerfile#L1) (C, 1-31;44-87): Pinned Python 3.14 base, archive fetch stage, nonroot user, config, OCI source/version/license, /healthz on 8000. **Implication:** Non-root and source revision labels.
- [compose.yaml](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/compose.yaml#L3) (C, 3-29): Explicit user 1000:1000, common /data media/download paths, /config, no-new-privileges, healthcheck and grace period comment. **Implication:** Prefer numeric --user; no separate mount hardlink assumption.
- [app/docker_entrypoint.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/docker_entrypoint.py#L3) (C, 3-39): Python entrypoint argument handling. **Implication:** Review startup and signal/umask behavior fully.
- [requirements.txt](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/requirements.txt#L12) (C, 12-39): Pinned FastAPI/Starlette/httpx/Pillow/rarfile/defusedxml/crypto/Argon2. **Implication:** Lock transitive graph and scan artifact.
- [.github/workflows/release.yml](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/.github/workflows/release.yml#L24) (C, 24-107): Release image scan, pinned Trivy action, tag guard and amd64/arm64 build. **Implication:** Gate immutable releases; no claim CI executed.
- [scripts/install_7zip.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/scripts/install_7zip.py#L26) (C, 26-95): 7-Zip 26.06 architecture pins, SHA256 metadata, HTTPS and bounded network download. **Implication:** Review binary license including unRAR restrictions, verify hashes.
- [.github/workflows/native-sqlite-build.yml](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/.github/workflows/native-sqlite-build.yml#L37) (C, 37-107): Native ARM64 runner check, reviewed source, image identity, SQLite provenance/hashes. **Implication:** Evidence must match actual platform and release, not emulated only.

## KV — Kareadita/Kavita

- [LICENSE](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/LICENSE#L73) (C, 73-621): GPL v3 text. **Implication:** Do not confuse GPL server with paid service entitlement.
- [Dockerfile](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Dockerfile#L3) (C, 3-48): Ubuntu noble, architecture-specific tar artifact, 5000 and healthcheck. **Implication:** Cross-platform artifact and native library tests.
- [entrypoint.sh](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/entrypoint.sh#L3) (L, 3-51): UID/GID logic commented out due Synology issues; optional jemalloc preload; exec Kavita. **Implication:** Do not claim active PUID/PGID support; runtime user explicit.
- [Kavita.Server/Kavita.Server.csproj](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Kavita.Server.csproj#L6) (C, 6-165): net10.0 and EF design package. **Implication:** Inspect dependency graph and packaging.
- [Kavita.Services/Kavita.Services.csproj](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Services/Kavita.Services.csproj#L20) (C, 20-44): Docnet.Core, NetVips/native, EF SQLite, EPUB, Hangfire, OIDC/JWT. **Implication:** Check PDF native, decoder and image library licenses.

## SW — Suwayomi/Suwayomi-Server

- [LICENSE](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/LICENSE#L4) (C, 4-232): MPL 2.0. **Implication:** Separate external runtime and source obligations.
- [server/build.gradle.kts](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/build.gradle.kts#L45) (C, 45-182): GraphQL/Javalin/Postgres, AndroidCompat, JCEF metadata and versioned JAR. **Implication:** Engine runtime dependency and webview requirements by release.
- [gradle/libs.versions.toml](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/gradle/libs.versions.toml#L5) (C, 5-56): JVM target 21, OkHttp 5.5.1 major locked by extensions. **Implication:** Do not assume newer JRE implies all extensions compatible.
- [scripts/resources/msi/suwayomi-server-x64.wxs](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/scripts/resources/msi/suwayomi-server-x64.wxs#L4) (C, 4-59): 64-bit MSI, JRE, JAR and launcher, downgrade guard. **Implication:** Versioned companion lifecycle with explicit data retention.
- [server/src/main/kotlin/eu/kanade/tachiyomi/LICENSE](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/eu/kanade/tachiyomi/LICENSE#L1) (C, 1-13): Apache-2.0 notice in Tachiyomi subtree. **Implication:** Preserve inherited file notices.
- [scripts/bundler.sh](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/scripts/bundler.sh#L31) (C, 31-100): Zulu JRE 25.0.5 and Electron 37.2.5 bundling; separate platform artifacts. **Implication:** JVM target differs from bundled JRE; audit exact binaries and notices.
- [.github/workflows/docker_build_stable.yml](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/.github/workflows/docker_build_stable.yml#L3) (L, 3-14): Published release dispatches separate suwayomi-server-docker repository workflow. **Implication:** Docker source/image must be independently pinned and audited.
- [scripts/resources/suwayomi-launcher.sh](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/scripts/resources/suwayomi-launcher.sh#L3) (C, 3-3): Launches bundled java with JVM exports and launcher JAR. **Implication:** No direct Android APK desktop assumption.

## MH — mihonapp/mihon

- [LICENSE](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/LICENSE#L66) (C, 66-176): Apache 2.0 copyright/patent grants, redistribution conditions. **Implication:** Preserve applicable notices and mark changes.
- [app/build.gradle.kts](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/build.gradle.kts#L34) (C, 34-156): Android build, 0.21.3 version string, universal and 4 ABI packages, package exclusions. **Implication:** Android-specific app not Windows binary; audit notices.
- [gradle/libs.versions.toml](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/gradle/libs.versions.toml#L3) (C, 3-28): Android Gradle, Compose, biometric, SQLite dependencies. **Implication:** Use interoperability not platform transplant.
- [.github/workflows/release.yml](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/.github/workflows/release.yml#L38) (C, 38-126): Java setup, universal/ABI-specific release and FOSS APK publication paths. **Implication:** Android build matrix separate from desktop/Docker.

## License decision gate

- KR: [GPL-3.0](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/LICENSE).
- MK: [GPL-3.0](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/LICENSE).
- UC: [MPL-2.0](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/LICENSE).
- KK: [AGPL-3.0-only](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/LICENSE).
- KV: [GPL-3.0](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/LICENSE).
- SW: [MPL-2.0](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/LICENSE).
- MH: [Apache-2.0](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/LICENSE).

GPL/AGPL covered derivative distributions require compliance with source and notice obligations; AGPL section 13 also applies to qualifying modified network-served covered software. GPL is not a noncommercial license. MPL obligations generally apply to covered files and modifications; check secondary-license restrictions and notices before combining; see [Mozilla FAQ](https://www.mozilla.org/en-US/MPL/2.0/FAQ/) and [MPL/GPL guidance](https://www.mozilla.org/en-US/MPL/2.0/combining-mpl-and-gpl/). Apache 2.0 requires applicable notices and changes marking and includes patent terms. No final legal determination or project license selection. Independent feature implementation with original code is proposed; do not copy protected expression or reuse assets without rights review. Separate process/container is not automatic license exemption.

### External verification on 2026-11-25

- [MangaBaka data license](https://mangabaka.org/about/data-license) distinguishes original and third-party data, NC and ShareAlike, including commercial ML usage; [dumps](https://mangabaka.org/developers/dumps) require attribution and warn schema unstable. Do not bundle the dataset or derived indexes until rights established.
- [MuPDF.js FAQ](https://mupdfjs.readthedocs.io/en/latest/faq/index.html) identifies AGPL/commercial licensing for JS wrapper and underlying WASM. UC's manifest declares mupdf ^1.28.6. Verify resolved package and legal scope before choosing PDF engine.
- Maki notices identify separate NC SPAN-S, GPL Maki weights with BSD provenance, BSD Real-ESRGAN, Apache OCR, MIT shaders and other native/browser notices. UC title-extractor code and weights separate; no assumption about operator-staged checkpoint rights.

## Remaining gates

Transitive dependency lock resolution and actual artifact SBOM, vulnerability scans, per-file headers, exact model and dataset provenance, provider/extension terms, runtime/JRE/OS redistribution, separate SW Docker repository source pin, signed release pipeline and actual Windows/Unraid smoke/upgrade/storage tests outstanding. No code copied, dependencies installed, weights downloaded, builds run or images published. 26 new feature candidates; total 356. G6 partial, G7 not executed. Next pass reconcile source/settings coverage and canonical feature selection, while keeping runtime and license gates explicit.
