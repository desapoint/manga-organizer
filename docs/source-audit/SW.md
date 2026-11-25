# SW — Suwayomi/Suwayomi-Server

Pinned revision: [4081b05f02456ec099c85127dfa5253e968d48a7](https://github.com/Suwayomi/Suwayomi-Server/tree/4081b05f02456ec099c85127dfa5253e968d48a7). Reviewed 2026-11-24 (Toronto).

## Scope

1,119 tracked blob paths enumerated without truncation. Four targeted implementation files inspected at this revision; declaration extracts and selected logic reviewed, not a line-by-line audit of every file. [Full path index](index/SW-files.csv), [machine-extracted settings/routes/symbols](index/SW-signals.csv) from [server/src/main/kotlin/suwayomi/tachidesk/manga/impl/download/DownloadManager.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/manga/impl/download/DownloadManager.kt) (13 signals). Classifications are heuristic. Runtime tests not run.

| Path class | Count |
|---|---:|
| build/deploy | 16 |
| config/resource | 27 |
| documentation | 13 |
| other | 54 |
| source candidate | 908 |
| test candidate | 101 |

Top-level distribution: server (679), AndroidCompat (379), scripts (23), .github (11), docs (7), buildSrc (3), gradle (3), .editorconfig (1), .gitattributes (1), .gitignore (1), CHANGELOG-TEMPLATE.md (1), CHANGELOG.md (1).

## Observations from source
- [ExtensionType.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/graphql/types/ExtensionType.kt#L25-L77): GraphQL contract includes storeIndexUrl, package, APK/JAR URL, library version, long version code, content warning, installed/update/obsolete flags and sources/store relation. repo, versionCode and isNsfw deprecated; new adapter must use modern fields when available and capability-detect older schema. IDs must preserve full width (serialize as strings at JavaScript boundaries).
- [ExtensionStoreService.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/manga/impl/extension/ExtensionStoreService.kt#L47-L257): fetch/upsert, refresh, DB/preference sync in both directions, extension list and gzip handling. Lifecycle and store formats warrant tests; not proof every package is safe.
- [DownloadManager.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/manga/impl/download/DownloadManager.kt#L74-L575): queue load/save, restore/resume, client updates, downloader per source, enqueue/dequeue/reorder/start/stop/clear. Integrate by reconciling engine state with our durable queue; avoid double schedulers. Inspect completion evidence and file location before marking imported.
- [SettingsRegistry.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/server-config/src/main/kotlin/suwayomi/tachidesk/server/settings/SettingsRegistry.kt#L11-L105): typed metadata registry and setting deprecation/migration classes; avoid hardcoding config keys across versions.
## Additional source-tree leads
AndroidCompat JVM compatibility layer and WebView, GraphQL types/operations, download file/archive/folder providers, backup protobuf export/import/validator and restore modes, user config/settings generation, content warnings, KOReader and OPDS types. Inspect auth/mutations and actual schema first; source-tree presence is not universal APK/JAR compatibility.
## Design implications
Required extension engine candidate, not reimplementation of Android. Version-pinned adapter, user-approved package sources, contract tests for both old/new schemas, authorized fixture and isolated staging. Preserve queue reconciliation after restart; no untrusted code inside core process.
