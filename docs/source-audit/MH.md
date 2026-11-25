# MH — mihonapp/mihon

Pinned revision: [f8e30a86086020aaee1e89a9d015373b1a4ff026](https://github.com/mihonapp/mihon/tree/f8e30a86086020aaee1e89a9d015373b1a4ff026). Reviewed 2026-11-24 (Toronto).

## Scope

1,483 tracked blob paths enumerated without truncation. Four targeted implementation files inspected at this revision; declaration extracts and selected logic reviewed, not a line-by-line audit of every file. [Full path index](index/MH-files.csv), [machine-extracted settings/routes/symbols](index/MH-signals.csv) from [domain/src/main/java/tachiyomi/domain/download/service/DownloadPreferences.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/domain/src/main/java/tachiyomi/domain/download/service/DownloadPreferences.kt) (13 signals). Classifications are heuristic. Runtime tests not run.

| Path class | Count |
|---|---:|
| build/deploy | 25 |
| config/resource | 263 |
| documentation | 8 |
| other | 218 |
| source candidate | 958 |
| test candidate | 11 |

Top-level distribution: app (742), icons (141), i18n (138), domain (116), core (74), presentation-core (72), data (60), presentation-widget (28), gradle (24), source-api (22), fastlane (14), .github (11).

## Observations from source
- [ExtensionManager.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/extension/ExtensionManager.kt#L120-L424): loaded/not-loaded lists, source-to-package map, find/check updates, language enablement, install/update/cancel/uninstall/trust, registered source lifecycle, store assignment. Native Android installation differs from SW server; trust semantics are useful, platform installer should not be ported blindly.
- [DownloadPreferences.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/domain/src/main/java/tachiyomi/domain/download/service/DownloadPreferences.kt#L15-L67): Wi-Fi only true, CBZ true, while-reading prefetch 0, read-removal slots -1, remove-marked-read false, remove-bookmarked false, category include/exclude, download new false, unread-only false, parallel source/page limits 5 each. Use device-side network policy and explicit cleanup retention; do not apply mobile defaults uncritically to NAS.
- [BackupManga.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/data/backup/models/BackupManga.kt#L13-L51): protobuf fields source long, source URL, descriptive metadata, chapters, categories, tracking, favorite, chapter/viewer flags, history, update strategy, favorite timestamp, excluded scanlators, notes, initialized/memo and next update. Import should preserve unrecognized fields or report loss, avoid restoring credentials or triggering downloads before review.
- [HttpSource.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/source-api/src/main/kotlin/eu/kanade/tachiyomi/source/online/HttpSource.kt#L28-L429): stable source ID generation, popular/search/latest, details/chapter/page lists and image retrieval. Source contract is more than search + download; filter schema and source preferences matter.
## Additional source-tree leads
Extension stores in domain/data, loader/installers including Shizuku, settings screens (appearance/browse/download/library/reader/search/security/tracking), backup options and restore handlers, download cache/provider/store/worker and queue, core archive and local source. Investigate tracker, chapter sort/filter, reader controls, update policy and localization; no Android build tested.
## Design implications
Mihon is client behavior/backup/schema reference. Use SW for compatible extensions on non-Android; import source IDs losslessly and report unsupported settings. Preserve bookmark exclusions and migration provenance.
