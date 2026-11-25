# Pinned source audit — passes 1–6

Reviewed 2026-11-24 Toronto. Extends [original documentation audit](../UPSTREAM-AUDIT.md) and [68 parent feature clusters](../FEATURE-MATRIX.md). No application code created. All seven pinned Git trees enumerated; targeted implementation declarations and selected logic examined (29 distinct source files in the first pass, followed by declaration extraction from 46 additional files; 75 unique files fetched). This is deeper than README review but not exhaustive implementation verification. No code copied, builds or tests executed.

| Repository | Tracked paths | First-pass signals | Report |
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

[Atomic feature register (330 rows)](ATOMIC-FEATURES.csv) is supplemental to the original parent matrix. Evidence L = targeted implementation logic observed, C = code declaration/route/setting inspected (not complete behavior), T = source-tree path candidate, not confirmed. Each item has a proposed acceptance criterion and pending owner decision. All current atomic evidence paths are pinned; two formerly ambiguous references were resolved in pass 3. Use the pinned report for context and line ranges. A parent is not approved by adding subfeatures. Required extension outcome remains unchanged. Automated extraction uses simple patterns in one focused file per project, not exhaustive route/config enumeration; dynamic/indirect routes, generated settings, comments and consumers need further review.

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

## Second pass — operational surface inventory

| Project | Additional files fetched | Extracted candidate signals | Report |
|---|---:|---:|---|
| KiranTheRam/mangarr | 7 | 317 | [KR pass 2](KR-pass2.md) |
| OrbitMPGH/Maki | 6 | 133 | [MK pass 2](MK-pass2.md) |
| AngeloSha/uchiyomi | 6 | 295 | [UC pass 2](UC-pass2.md) |
| Kha-kis/manga-arr | 7 | 259 | [KK pass 2](KK-pass2.md) |
| Kareadita/Kavita | 6 | 235 | [KV pass 2](KV-pass2.md) |
| Suwayomi/Suwayomi-Server | 6 | 453 | [SW pass 2](SW-pass2.md) |
| mihonapp/mihon | 8 | 342 | [MH pass 2](MH-pass2.md) |
| Total | 46 | 2,034 | |

Combined with 539 first-pass signals: **2,573 extracted identifiers/candidate declarations**. These include settings, routes, functions, local variables and schema fields; they are not 2,573 distinct implemented features. The updated atomic register has **172 candidate rows** (60 added in this pass); some rows intentionally overlap across reference implementations and must be merged before development. Evidence levels and pending decisions remain explicit.

### Additional findings

- KR: manual file/range mapping, cleanup preview, group listing, additional series folders, per-series source linking, volume merge/resync preview and import-list skip workflow deserve independent requirements.
- KK: separate quality score matrices, format preview, delay order, default profiles, connection test and circuit-reset operations confirm the depth of configuration surfaces.
- MK: root routing rules with order/test, auto-tag preview/apply, drop-folder settings/manual run, quality-format CRUD and OPDS token settings expand the initial UX comparison.
- UC: distinguish admin repair runs, source gap scans, fills, refetch, job cancellation and offline download manifests; personal tokens and reader profiles are separate surfaces. GraphQL operations declarations are indexed for future adapter contracts.
- KV: OPDS smart-filter/collection browsing, KOReader ebook hash sync, Tachiyomi latest/mark-until API and reading-profile parent/promotion operations are present as routes. Individual permissions and behavior still need review.
- SW: server/user config declarations, GraphQL download/source/chapter types and extension implementation are now indexed. Inspect settings consumers and generated schema; do not treat local variables as configuration options.
- MH: reader settings include orientation, scaling, zoom, transitions, page flash, navigation positioning and Wi-Fi prefetch; library settings include column layout, sort, update restrictions, duplicate-read policy, filters and badges. Security, tracker and backup schema declarations are separately indexed.

### Unresolved evidence paths

Resolved in pass 3: A051 bff/src/routes/titleExtraction.ts; A052 bff/src/routes/numbering.ts. The 23 historical T-level entries now have C/L source evidence.

### Mandatory gate

Source audit remains open. Full path enumeration and source retrieval are not exhaustive logic review. Remaining work: inspect all route implementations and settings consumers, provider and client adapters, permissions and migrations, actual releases and tests; verify runtime on target platforms; resolve duplicate candidates and obtain owner approval. No implementation has started.

## Focused follow-up: physical files and duplicates

The owner explicitly required file organization and duplicate handling. [File organization and duplicate management](../FILE-ORGANIZATION.md) records five additional pinned implementation-file inspections (KR rename, MK duplicate groups and record repair, UC fingerprint, KK rename planner), their limitations, and proposed unified requirements. This supplements the historical pass 1/2 metrics above; it is not a claim of runtime testing or exhaustive upstream parity.

## Pass 3 — continued audit

[Pass 3 findings](PASS3.md), [organization evidence CSV](PASS3-EVIDENCE.csv), and [mandatory completion ledger](COMPLETION-LEDGER.md). 51 additional source/test files retrieved, all 23 prior filename-only candidates resolved to source evidence, seven latest published release notes recorded, and 24 additional atomic candidates added. Current register has 196 rows; historical counts above are retained for traceability. Runtime and exhaustive settings/integration/logic gates remain open. No implementation.

## Pass 4 — behavior and release contracts

[Pass 4 report](PASS4.md), [pinned evidence CSV](PASS4-EVIDENCE.csv), [release comparison](RELEASE-COMPARISON.csv). 48 pinned source files reviewed (includes repeat retrievals), seven release source files checked, all seven release-to-audited-commit comparisons recorded, 43 additional candidate rows. Current register 239 rows. Corrected Mihon duplicate SQL observation. Maki audited commit matches release; others ahead/diverged. Static gates remain open; no tests run or application code.

## Pass 5 — integration contracts

[Pass 5](PASS5.md), [evidence CSV](PASS5-EVIDENCE.csv), [integration inventory](INTEGRATION-INVENTORY.csv): 51 distinct pinned files inspected (includes repeat retrievals), 25 integration groups, 42 added candidate rows; total 281. Separates metadata, source, list import, release indexer, client, tracker and external reader. Observed seven SW and twelve MH tracker registrations, not verified runtime or shipped parity. All remaining gates open.

## Pass 6 — reader and database architecture

[Pass 6](PASS6.md), [evidence CSV](PASS6-EVIDENCE.csv), [reader and data contracts](READER-DATA-CONTRACTS.md). 49 distinct pinned files reviewed including repeat retrievals; 49 added candidates, total 330. Separates logical media identity, physical bytes, presentation, user progress and privacy. No executed migrations/runtime tests, no approved stack or new optional features. Gates remain open.
