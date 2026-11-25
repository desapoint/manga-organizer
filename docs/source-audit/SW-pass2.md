# SW — second-pass settings and operational routes

Pinned [revision](https://github.com/Suwayomi/Suwayomi-Server/tree/4081b05f02456ec099c85127dfa5253e968d48a7). Source contents fetched for the files below; patterns enumerate declarations and candidate fields only, not verified runtime semantics. Scope: 6/6 files. See [signals](index/SW-pass2.csv).

| File | Lines | Signals |
|---|---:|---:|
| [server/server-config/src/main/kotlin/suwayomi/tachidesk/server/ServerConfig.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/server-config/src/main/kotlin/suwayomi/tachidesk/server/ServerConfig.kt) | 1239 | 115 |
| [server/server-config/src/main/kotlin/suwayomi/tachidesk/server/settings/UserConfig.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/server-config/src/main/kotlin/suwayomi/tachidesk/server/settings/UserConfig.kt) | 342 | 47 |
| [server/src/main/kotlin/suwayomi/tachidesk/graphql/types/DownloadType.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/graphql/types/DownloadType.kt) | 211 | 30 |
| [server/src/main/kotlin/suwayomi/tachidesk/graphql/types/SourceType.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/graphql/types/SourceType.kt) | 499 | 112 |
| [server/src/main/kotlin/suwayomi/tachidesk/graphql/types/ChapterType.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/graphql/types/ChapterType.kt) | 142 | 28 |
| [server/src/main/kotlin/suwayomi/tachidesk/manga/impl/extension/Extension.kt](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/manga/impl/extension/Extension.kt) | 803 | 121 |

Review each route's permissions, validation, service calls, settings defaults and UI binding before acceptance. No runtime testing.
