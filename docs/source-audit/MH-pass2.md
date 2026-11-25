# MH — second-pass settings and operational source

Pinned [revision](https://github.com/mihonapp/mihon/tree/f8e30a86086020aaee1e89a9d015373b1a4ff026). Source contents fetched for the files below; patterns enumerate declarations and candidate fields only, not runtime semantics. Scope: 8/8 requested filenames; unresolved: none. See [signals](index/MH-pass2.csv).

| File | Lines | Signals |
|---|---:|---:|
| [domain/src/main/java/tachiyomi/domain/library/service/LibraryPreferences.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/domain/src/main/java/tachiyomi/domain/library/service/LibraryPreferences.kt) | 259 | 48 |
| [app/src/main/java/eu/kanade/tachiyomi/ui/reader/setting/ReaderPreferences.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/ui/reader/setting/ReaderPreferences.kt) | 457 | 84 |
| [core/common/src/main/kotlin/eu/kanade/tachiyomi/core/security/SecurityPreferences.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/core/common/src/main/kotlin/eu/kanade/tachiyomi/core/security/SecurityPreferences.kt) | 44 | 5 |
| [app/src/main/java/eu/kanade/domain/track/service/TrackPreferences.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/domain/track/service/TrackPreferences.kt) | 62 | 12 |
| [app/src/main/java/eu/kanade/tachiyomi/data/backup/create/BackupOptions.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/data/backup/create/BackupOptions.kt) | 117 | 19 |
| [app/src/main/java/eu/kanade/tachiyomi/data/backup/models/BackupChapter.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/data/backup/models/BackupChapter.kt) | 64 | 12 |
| [app/src/main/java/eu/kanade/tachiyomi/extension/util/ExtensionLoader.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/extension/util/ExtensionLoader.kt) | 533 | 62 |
| [app/src/main/java/eu/kanade/tachiyomi/data/download/Downloader.kt](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/data/download/Downloader.kt) | 854 | 100 |

Review the actual preference consumers and default behavior, loader trust, schema versions, UI bindings and tests. No runtime testing.
