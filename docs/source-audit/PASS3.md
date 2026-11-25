# Pass 3 — close filename-only leads; organization, duplicates, tests and releases

Date: 2026-11-24 Toronto. **Audit continues; no implementation.** All 23 previously T-level candidates now have inspected source declarations or targeted logic. Two ambiguous Uchiyomi references resolved to the actual route files. Narrowed compound labels where the inspected file supports only part of the earlier wording. This resolves the *filename-only inventory* gate, not every implementation, settings consumer or runtime gate.

## Targeted organization evidence (25 implementation files)

[Machine-readable evidence](PASS3-EVIDENCE.csv) (file/line references, evidence levels, observations and design implications).

### KR — KiranTheRam/mangarr

- [backend/mangarr/library/scanner.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/library/scanner.py#L149) (L; lines 149-397): Folder normalization and matching; creates chapter rows only when disk evidence is unambiguous; exact chapter files outrank volume coverage and existing matched file is preferred. **Implication:** Preserve manually chosen copies, distinguish coverage and report unmatched.
- [backend/mangarr/library/matcher.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/library/matcher.py#L138) (L; lines 138-517): Read-only matching, PDF explicit mapping/alias logic, archive member evidence, ambiguous filename results left unmatched; actual ZipInfo used for duplicate member names. **Implication:** Reject ambiguous mappings and check manifest/page order before destructive resolution.
- [backend/mangarr/library/publish.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/library/publish.py#L10) (L; lines 10-35): Exclusive sibling-file publish via Linux renameat2 or macOS exclusive rename, with link fallback where available. **Implication:** Separate no-clobber publication from ordinary move; test target OS/filesystem and never silently overwrite.
- [backend/mangarr/library/volume_merge.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/library/volume_merge.py#L1) (C; lines 1-116): Module documents DB update before chapter cleanup so interruption leaves duplicates instead of missing data. **Implication:** Read full merge transaction and provenance before treating it as verified.

### MK — OrbitMPGH/Maki

- [src/Maki.Api/Services/ChapterPageHashService.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Services/ChapterPageHashService.cs#L12) (L; lines 12-219): All source pages must arrive and decode for ordered perceptual page/strip evidence; missing pages invalidate set, failed hashes cool down, budgets and source holds exist. **Implication:** Perceptual evidence is reconciliation, not byte equality; do not download solely to compare local copies without opt-in.
- [src/Maki.Core/Io/FileIdentity.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Core/Io/FileIdentity.cs#L16) (L; lines 16-39): Compares underlying file identities; comments identify CIFS/FUSE fabricated-inode risk. **Implication:** Return same/different/unknown and fail closed when storage identity unreliable.
- [src/Maki.Api/Services/SeriesRenameService.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Services/SeriesRenameService.cs#L15) (L; lines 15-230): Plan fingerprint SHA-256; folder/file selection, unmatched files untouched, conflict detection, expected preview fingerprint API; case-only temp suffix. **Implication:** Require current plan validation, never rename unrecognized files silently.

### UC — AngeloSha/uchiyomi

- [bff/src/lib/pageHash.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/pageHash.ts#L4) (L; lines 4-157): dHash exact equality used for repeated page furniture within one series; low-information hashes rejected; repeated distinct chapters required. **Implication:** Credit/advertisement skip feature is not a duplicate deletion algorithm; no weak perceptual match as deletion authority.
- [bff/src/lib/healthFiles.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/healthFiles.ts#L37) (C; lines 37-49): Chapter-file health check and sorted findings. **Implication:** Inspect classification and repair callers before claiming parity.
- [bff/src/lib/fingerprintJob.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/fingerprintJob.ts#L1) (L; lines 1-226): Incremental fingerprint backfill, unknown values do not match, scan-cleared stale fingerprints, unavailable roots deferred, repeated scheduled passes. **Implication:** Persist scan work and stale/unknown status, defer inaccessible roots.
- [bff/src/routes/titleExtraction.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/routes/titleExtraction.ts#L24) (C; lines 24-70): Capabilities, batch run/status/cancel, cover fingerprint, preview/mask and apply endpoints. **Implication:** Separate title artwork extraction from metadata identification; verify entitlement/model licenses.
- [bff/src/routes/numbering.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/routes/numbering.ts#L81) (C; lines 81-179): Extension source preferences and admin numbering preview/apply routes; comments explicitly protect against active download rename races. **Implication:** Use one mutation authority and require reviewed renumber plans.

### KK — Kha-kis/manga-arr

- [app/file_identity.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/file_identity.py#L33) (C; lines 33-224): Strict identity schema and errors; storage profile binds qualification digest and client/server mount contract; native handle serialization. **Implication:** Mount identity needs explicit qualification; do not infer proof from path.
- [app/file_mutation_lock.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/file_mutation_lock.py#L39) (L; lines 39-145): Sidecar mutation guard validates DB/parent/lock device and inode and exposes a subprocess descriptor; avoids unmanaged SQLite FD lock interactions. **Implication:** Single mutation lease with identity revalidation; platform-specific locking needs tests.
- [app/volume_assembly.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/volume_assembly.py#L54) (L; lines 54-188): Per-source and page SHA-256 evidence, ordered range digests, ambiguous page numbers rejected, streaming hashes and source-change evidence. **Implication:** Prove unique coverage and verify final volume before any removal; not a blanket volume-vs-chapter dedup.
- [app/import_publication.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/import_publication.py#L80) (C; lines 80-319): FileFingerprint, prepared/staged/final artifacts, journal states and checks for unresolved publication/quarantine; no-replace primitive declarations. **Implication:** Full state machine and execution/replay need inspection; test each crash boundary.

### KV — Kareadita/Kavita

- [Kavita.Services/Scanner/MountDetectorService.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Services/Scanner/MountDetectorService.cs#L17) (L; lines 17-96): Linux/macOS mount detection, longest covering mount including stacked mounts, escaped path handling. **Implication:** Detect mount replacement, not just directory existence; Windows/Unraid specifics require qualification.
- [Kavita.Services/Scanner/FolderValidationService.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Services/Scanner/FolderValidationService.cs#L22) (C; lines 22-171): Dedicated folder validation service present. **Implication:** Read complete validation and platform consumers.
- [Kavita.Services/Scanner/ScannerService.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Services/Scanner/ScannerService.cs#L131) (L; lines 131-624): Targeted scan can abort on ambiguous ownership; mount/drive guards before destructive reconciliation, emits inaccessible-root events. **Implication:** Distinguish intentionally emptied roots from outages; protect inventory on unavailable shares.

### SW — Suwayomi/Suwayomi-Server

- [server/src/main/kotlin/suwayomi/tachidesk/manga/impl/download/fileProvider/impl/ArchiveProvider.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/manga/impl/download/fileProvider/impl/ArchiveProvider.kt#L29) (C; lines 29-132): Archive image listing/streaming, extraction, successful download handling, delete, archive stream and size surface. **Implication:** Contract for engine-managed staging; verify actual path, completion and ownership.
- [server/src/main/kotlin/suwayomi/tachidesk/manga/impl/download/fileProvider/impl/FolderProvider.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/manga/impl/download/fileProvider/impl/FolderProvider.kt#L29) (C; lines 29-111): Folder image/stream and archive output plus delete and successful download hooks. **Implication:** Allow folder and archive outputs without treating engine cache as library ownership.

### MH — mihonapp/mihon

- [domain/src/main/java/tachiyomi/domain/manga/interactor/GetDuplicateLibraryManga.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/domain/src/main/java/tachiyomi/domain/manga/interactor/GetDuplicateLibraryManga.kt#L9) (L; lines 9-14): Duplicate library candidate query by current manga ID and lowercase title. **Implication:** Bibliographic suggestion, not physical duplicate proof.
- [app/src/main/java/eu/kanade/tachiyomi/util/chapter/ChapterRemoveDuplicates.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/util/chapter/ChapterRemoveDuplicates.kt#L6) (C; lines 6-16): List-level duplicate chapter removal utility. **Implication:** Distinguish reader list filtering from file deletion.
- [app/src/main/java/eu/kanade/tachiyomi/data/download/DownloadProvider.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/data/download/DownloadProvider.kt#L113) (L; lines 113-341): Source/manga/chapter directory resolution, batch lookup, legacy naming permutations, non-ASCII and optional chapter URL hash suffix. **Implication:** Import legacy paths without losing identity; six-character name hash is not content proof.
- [app/src/main/java/eu/kanade/tachiyomi/data/backup/restore/BackupRestorer.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/data/backup/restore/BackupRestorer.kt#L72) (L; lines 72-147): Restore options select categories/preferences/source settings/manga/stores, invalidates download cache after restore. **Implication:** Selective reviewed restore, cache invalidation and no implicit acquisition.

## Previously filename-only candidates — all 23 addressed

| ID | Pinned evidence | Observation |
|---|---|---|
| A032 | [DropFolderImportJob.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Jobs/DropFolderImportJob.cs#L11) | Job delegates to import service; filesystem ownership and idempotency not established. |
| A033 | [CustomRailsController.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Controllers/CustomRailsController.cs#L25) | List/create/update/delete/items/count; placement and spec fields. |
| A034 | [SeriesRequestsController.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Controllers/SeriesRequestsController.cs#L48) | Request list/create and admin pending count/edit/approve; shortlist is separate. |
| A035 | [NotesController.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Controllers/NotesController.cs#L15) | Notes, page index, visibility, counts and replies; page-share settings separate. |
| A036 | [FollowedCreatorReleaseJob.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Jobs/FollowedCreatorReleaseJob.cs#L13) | Scheduled creator-release job; related-work watch is separate. |
| A047 | [archivePace.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/archivePace.ts#L17) | Pace defaults, hourly bounds, gap/backoff and time-window functions; use authorized rate limits only. |
| A048 | [sourceWatchdog.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/sourceWatchdog.ts#L77) | Source check progress, run/start/sweep and optional auto-fix surface. |
| A049 | [chapterCleanup.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/chapterCleanup.ts#L121) | Cleanup settings, due count, kept reasons, tombstone and restored-file functions; eligibility still needs review. |
| A050 | [privateLibrary.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/routes/privateLibrary.ts#L18) | Private series, management and privacy routes; verify ACLs and caching. |
| A051 | [titleExtraction.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/routes/titleExtraction.ts#L24) | Resolved to route for preview/mask/apply and batch operations. |
| A052 | [numbering.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/routes/numbering.ts#L81) | Resolved to admin preview/apply and source preference routes. |
| A063 | [import_staging.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/import_staging.py#L106) | Stage, rename, prepare for mutation, commit_all, rollback and worker helper; inspect crash boundaries. |
| A064 | [private_file_claim.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/private_file_claim.py#L45) | Strict serialized full fingerprint/claim binding and guard verification interface; pack claims separate. |
| A065 | [volume_assembly.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/volume_assembly.py#L54) | Ordered chapter and range evidence; cleanup separate and not qualified here. |
| A066 | [metadata_provenance.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/metadata_provenance.py#L105) | Field priorities, candidate recording, selection and manual metadata; precedence details not yet verified. |
| A077 | [AnnotationController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/AnnotationController.cs#L33) | Filtered browse, chapter/series retrieval, create/update/like/unlike/delete. |
| A078 | [PanelsController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/PanelsController.cs#L24) | API-key progress save/get. |
| A080 | [PersonController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/PersonController.cs#L37) | Person search, roles, filtered browse, admin edit and known series. |
| A081 | [CBLController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/CBLController.cs#L51) | File/URL/repository import, revalidation, finalize and remap rules. |
| A082 | [ColorScapeController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/ColorScapeController.cs#L10) | Authorized color palette endpoints for series/volume/chapter/person/list/collection; fonts separate. |
| A094 | [ProtoBackupValidator.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/manga/impl/backup/proto/ProtoBackupValidator.kt#L22) | Backup validation entry points; export and import are separate implementation files. |
| A095 | [ArchiveProvider.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/manga/impl/download/fileProvider/impl/ArchiveProvider.kt#L29) | Archive file provider operations; companion folder provider separately inspected. |
| A112 | [BackupFileValidator.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/data/backup/BackupFileValidator.kt#L12) | Backup validation results entry point; restore options separate. |

Four of these (A051/A052/A065/A095) overlap the 25-file organization pass; 19 other files fetched. C/L remain static evidence only. Other compound claims (page-share settings, shortlist, related work, pack claims, volume cleanup, fonts, backup export/import) require their own module inspection and must not be inferred from adjacent code.

## Upstream test-source review (7 additional files)

These are observed test definitions, **not executed or proven passing**.

| Repository | Test file | Observed test cases |
|---|---|---|
| KR | [test_rename.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/tests/test_rename.py#L21) | Extension preservation, numeric ordering, shared volume archive one rename, collisions skipped, case-only rename. |
| MK | [HealthDuplicateTests.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/tests/Maki.Api.Tests/HealthDuplicateTests.cs#L72) | Keep linked copy, no spare without valid survivor, changed file or ownership after preview refused, path aliases and inaccessible files handled. |
| UC | [fingerprint.test.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/test/fingerprint.test.ts#L40) | Recompression survives, rename changes fingerprint, entry order ignored, common cover not enough, unreadable/empty yields null. |
| KK | [test_import_publication_journal.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/tests/python/test_import_publication_journal.py#L693) | Stale snapshots, fsync ordering, SIGKILL/replay, missing or changed stage, live-owner deferral, replaced source preserved. |
| KV | [ScannerServiceScanFolderDedupeTests.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Services.Tests/ScannerServiceScanFolderDedupeTests.cs#L43) | Coalesces duplicate scan requests, concurrent request handling, running scan follow-up, ambiguous folder abort, watcher debounce cap. Job dedup not physical-file dedup. |
| SW | [ArchiveProviderTest.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/test/kotlin/suwayomi/tachidesk/manga/impl/ArchiveProviderTest.kt#L41) | Missing archive, directory and invalid ZIP errors, file-only entry count, image name order. |
| MH | [DownloadProviderTest.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/test/java/eu/kanade/tachiyomi/data/download/DownloadProviderTest.kt#L37) | Legacy chapter naming before 0.21.0 and 0.21.0/0.21.1 compatibility. |

## Published release inventory

GitHub latest published release endpoints retrieved 2026-11-24 Toronto; these were non-prerelease entries. Release notes are maintainers' claims, not runtime verification. Source audits use separate pinned commits; do not infer release parity. Dates UTC.

| Repo | Release | Published | Notes |
|---|---|---|---|
| KR | [v0.2.0](https://github.com/KiranTheRam/mangarr/releases/tag/v0.2.0) | 2026-11-04 | Release notes describe source additions, group/monitor preferences, duplicate-page fallback, bulk imports and volume/image operations. |
| MK | [v0.38.0](https://github.com/OrbitMPGH/Maki/releases/tag/v0.38.0) | 2026-11-24 | Notes describe page comparison for split chapters, expanded discovery, rating dimensions and upscaling. |
| UC | [v0.109.0](https://github.com/AngeloSha/uchiyomi/releases/tag/v0.109.0) | 2026-11-23 | Notes describe Komga filtered list routes respecting access, large-library scanning improvements and cover title extraction. |
| KK | [v1.3.2](https://github.com/Kha-kis/manga-arr/releases/tag/v1.3.2) | 2026-10-08 | Published release explicitly names source 091b2b12ea235eb0d62306bce94b7e5d70746f4c, different from audited branch. Maintainers report qualification; not independently reproduced. |
| KV | [v0.9.1.4](https://github.com/Kareadita/Kavita/releases/tag/v0.9.1.4) | 2026-09-02 | Notes explicitly identify Hardcover recommendations and OAuth as Kavita+ features, and completed-chapter opening without immediate progress reset. |
| SW | [v2.4.2373](https://github.com/Suwayomi/Suwayomi-Server/releases/tag/v2.4.2373) | 2026-11-21 | Notes mention launcher correction, CSRF, JRE security update and randomized temporary file URLs. |
| MH | [v0.21.3](https://github.com/mihonapp/mihon/releases/tag/v0.21.3) | 2026-10-25 | Release body warns that future major versions will remove legacy Tachiyomi .tachibk backup and 1.4-extension compatibility. Treat legacy and current formats as different contracts; do not assume branch behavior matches release. |

Version strategy: explicitly negotiate Suwayomi schema, store API, long identifiers and package version; maintain fixture-based Mihon legacy/current backup and extension compatibility matrix. Do not promise every legacy extension works. Do not copy premium code or depend on paid entitlements without an explicit decision. Compare released tag trees, build manifests and license notices before implementation.

## Consolidated decision

Organization and duplicates require distinct physical identity, exact content identity, similar-page evidence and bibliographic identity. KR/KK provide conservative plan/publication references; MK provides exact duplicate-group/relink and page-reconciliation models; UC provides incremental fingerprint and low-information hash guards; KV provides mount protection/scanner design; SW/MH provide download layout and migration interoperability. These are complementary rather than interchangeable.

51 distinct additional source/test files retrieved this pass (25 + 19 + 7); selected logic/declarations/test cases inspected, not exhaustive full-file logic review. Prior 75-file retrieval figure and five organization files are historical counts. Atomic inventory now 196 rows, including 24 additional organization and duplicate-related or migration candidates. No extra owner approvals. See [completion ledger](COMPLETION-LEDGER.md).
