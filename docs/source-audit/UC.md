# UC — AngeloSha/uchiyomi

Pinned revision: [dc5d52212cbe7ad1984fbb29d1490e524f886e0c](https://github.com/AngeloSha/uchiyomi/tree/dc5d52212cbe7ad1984fbb29d1490e524f886e0c). Reviewed 2026-11-24 (Toronto).

## Scope

1,894 tracked blob paths enumerated without truncation. Four targeted implementation files inspected at this revision; declaration extracts and selected logic reviewed, not a line-by-line audit of every file. [Full path index](index/UC-files.csv), [machine-extracted settings/routes/symbols](index/UC-signals.csv) from [bff/src/lib/sources/suwayomi/extensions.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/sources/suwayomi/extensions.ts) (13 signals). Classifications are heuristic. Runtime tests not run.

| Path class | Count |
|---|---:|
| build/deploy | 34 |
| config/resource | 32 |
| documentation | 86 |
| other | 158 |
| source candidate | 725 |
| test candidate | 859 |

Top-level distribution: bff (820), web (746), desktop (100), docs (67), ios (65), scripts (33), .github (15), deploy (15), services (11), logo-options (7), templates (2), .dockerignore (1).

## Observations from source
- [extensions.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/sources/suwayomi/extensions.ts#L13-L168): extension info includes package, language, version, install/update/obsolete/content flags and repo; facts distinguish APK/JAR URL and version code; refresh/list/action, source membership and add/list stores use GraphQL. Generation guards around mutations merit concurrency tests.
- [client.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/sources/suwayomi/client.ts#L38-L267): validated base/origin helpers, auth abstraction, enabled/configured checks, transport and engine refusal errors, GraphQL timeout and read-only detection. Distinguish unavailable engine, auth, unsupported query, mutation outcome. Do not retry ambiguous writes blindly.
- [progressRules.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/progressRules.ts#L3-L33): completion at penultimate page when known page count exceeds one, not exact end; comment says dedup completed chapters rather than count events. Offline event times rejected if over 90 days old, future capped at server now. Candidate: configurable completion rule, explicit user mark, monotonic event time and dedup; test one-page chapters and credit pages.
- [health.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/health.ts#L104-L472): structured health findings/checks, per-series gap results, recency and outcome classification; 100-number cap and seven-day freshness constants. Do not confuse unfillable/unknown with healthy. Source-specific repair behavior remains to inspect.
## Additional source-tree leads
Archive pace/caps/plan, partial chapter fallback, chapter failures, extension pin/browser/install lock/site sessions, source standing/probe/watchdog/diagnosis, private library, page cleanup, numbering, title extraction, external readers, OPDS, Komga compatibility, KOSync, recommendations, translation, backup extensions and copy routing. Inspect UI, DB migrations, permissions, repair tests and desktop engine shim. No solver bypass implementation recommended.
## Design implications
Best extension administration and source diagnostics reference. Offline progress and health need explicit invariants before any automatic deletion. Use a trusted isolated engine and preserve independently managed files.
