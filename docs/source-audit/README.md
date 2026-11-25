# Pinned source audit — pass 1

Reviewed 2026-11-24 Toronto. Extends [original documentation audit](../UPSTREAM-AUDIT.md) and [68 parent feature clusters](../FEATURE-MATRIX.md). No application code created. All seven pinned Git trees enumerated; targeted implementation declarations and selected logic examined (four files per project, 28 total). This is deeper than README review but not exhaustive implementation verification. No code copied, builds or tests executed.

| Repository | Tracked paths | Focus-file signals | Report |
|---|---:|---:|---|
| [KiranTheRam/mangarr](https://github.com/KiranTheRam/mangarr/tree/09238b58405fb94a5c753405475e30a3b4d5cb78) | 244 | 52 | [KR](KR.md) |
| [OrbitMPGH/Maki](https://github.com/OrbitMPGH/Maki/tree/0de45e926f368e23ace4dba04e2211d1de83aadb) | 3755 | 231 | [MK](MK.md) |
| [AngeloSha/uchiyomi](https://github.com/AngeloSha/uchiyomi/tree/dc5d52212cbe7ad1984fbb29d1490e524f886e0c) | 1894 | 13 | [UC](UC.md) |
| [Kha-kis/manga-arr](https://github.com/Kha-kis/manga-arr/tree/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef) | 1098 | 178 | [KK](KK.md) |
| [Kareadita/Kavita](https://github.com/Kareadita/Kavita/tree/7fcf1085a5355a8c592d17adcfbb28b69276ae40) | 4123 | 39 | [KV](KV.md) |
| [Suwayomi/Suwayomi-Server](https://github.com/Suwayomi/Suwayomi-Server/tree/4081b05f02456ec099c85127dfa5253e968d48a7) | 1119 | 13 | [SW](SW.md) |
| [mihonapp/mihon](https://github.com/mihonapp/mihon/tree/f8e30a86086020aaee1e89a9d015373b1a4ff026) | 1483 | 13 | [MH](MH.md) |
| Total | 13716 | 539 | |

## Selection inventory

[Atomic feature register (112 rows)](ATOMIC-FEATURES.csv) is supplemental to the original parent matrix. Evidence L = targeted implementation logic observed, C = code declaration/route/setting inspected (not complete behavior), T = source-tree path candidate, not confirmed. Each item has a proposed acceptance criterion and pending owner decision. File basenames resolve through linked per-project full path indexes; use the pinned report for exact locations. A parent is not approved by adding subfeatures. Required extension outcome remains unchanged. Automated extraction uses simple patterns in one focused file per project, not exhaustive route/config enumeration; dynamic/indirect routes, generated settings, comments and consumers need further review.

## Key cross-project findings and conflicts

| Area | Strong reference | Design observation |
|---|---|---|
| Acquisition authority | KK | Exact client/protocol/download identity, explicit intent; unknown must not become permission. |
| Pack matching | KR | Inspect actual coverage, not name only. |
| Unified UX and policy | MK | Custom adapter stores distinct from Suwayomi extensions; upgrade profiles, bulk hold, per-client seeding policy. |
| Extension lifecycle | UC/SW/MH | UC GraphQL bridge, SW actual runtime, MH trust and source schema; version negotiation, long IDs, APK/JAR differences, store trust and permissions. |
| Reader and progress | UC/KV/MH | UC penultimate-page completion differs from explicit progress; don't mark read via prefetch. MH bookmark/category cleanup protections; KV explicit read/unread and bookmarks. |
| External devices | KV/MK/UC | Device API contracts and progress-owner mapping; sync without echo/identity collisions. |
| Storage safety | KK/KR | Plan, stage, validate and publish; no destructive defaults, fenced mounts, seeding protection, backup/restore. |
| Recommendations | MK | Local catalog/models, separate licensing, resource qualification. |
| Source reliability | UC | Structured diagnosis, health freshness and alternate sources; respect access controls. |

## What remains before implementation

1. Walk all source paths by module; inspect route registration/auth, settings defaults + consumers + UI, provider adapters and error paths, schema/migration history, tests and deployment. Full file manifests make omissions visible but are not code review.
2. Resolve all T rows to code evidence or remove them; add one atomic row per settings control/route/integration behavior; map repeated features to one internal requirement; reconcile parent matrix (e.g. MK upgrade profile and KK Suwayomi handling).
3. Re-read release tags, compare default branch to shipped releases, verify paid features and third-party license restrictions. No runtime claims from filenames.
4. Build synthetic/authorized fixtures and execute cross-project functional comparisons (Windows, Linux/Unraid, database restart, NAS/path recovery, extension contract, reader sync). Record environment, actual results and failures. No source access-control bypass.
5. Approve scope, architecture, license and selected features before implementation.

## Audit status

- [x] All seven pinned tracked trees enumerated without truncation.
- [x] Targeted source pass covering domain/config, extension APIs, import authority and reader/device contracts.
- [x] Persist raw-path metadata inventory, focused symbol/key/route inventory, per-project observations and granular selection leads.
- [ ] Exhaustive code inspection and tests per repository.
- [ ] Runtime comparisons / owner feature approvals.
