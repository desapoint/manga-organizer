# Pass 12 — feature reconciliation and selection inventory

2026-11-25. Continues Pass 11 at eea02d7c7960e2d26fcf17bb666215c3e0693732. Documentation only. This pass reconciles the 356 historical atomic candidates and all 70 parent rows, preserving decisions. All 27 settings groups linked to related parent categories. No upstream code copied, no tests run.

## Deliverables and validation

- [Canonical inventory](CANONICAL-SELECTION.csv): 126 proposed capability groups (58 multi-member, 68 singleton). Preserves 32 Pass 8 groups and adds 26 cross-reference groups. Grouping is a review aid, not exact feature equivalence; a broad topic may contain independently selectable features.
- [Atomic membership](CANONICAL-MEMBERSHIP.csv): exactly 356 rows, all original columns retained verbatim and mapped once, with parent selection preserved. Existing atomic and parent registers unchanged.
- [Parent selection crosswalk](PARENT-SELECTION-CROSSWALK.csv): all 70 parent rows, capability IDs, grouped settings IDs, gap evidence and proposed dependencies.
- [Settings selection crosswalk](SETTINGS-SELECTION-CROSSWALK.csv): all 27 Pass 11 records, source/default/validation/consumer evidence preserved, assigned stable S IDs and related parents. Links are thematic, not proof that every setting implements every mapped parent.
- [Previously unmapped parent evidence](PASS12-GAP-EVIDENCE.csv): six parent categories now have supplemental pinned upstream observations; F68 has an explicitly proposed-only contract. These are separate evidence records, not silently added atomic features. Historical PARENT-COVERAGE.csv and coverage baseline remain snapshots; zero original atomic rows in seven parents still true.

Validation: no duplicate/missing atomic membership; all member IDs resolved; all 70 parents represented and 27 settings records retained. All 356 original atomic decisions retained; five parent outcomes Required (F12,F57,F58,F69,F70), 65 Pending. 126 groups do not measure completion or implemented scope. No optional features or stack/license selected.

## Previously unmapped parents

- **F04 — Language-edition linking and visible per-user progress** (L): Query scopes siblings by privacy/visibility and computes completed chapter position per viewer; work linking declarations at L42-L54. Remaining: Separate editions, folders, language and progress; verify unique index and linking mutations. [pinned source](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/editions.ts#L148-L174)
- **F15 — Newznab indexer types and Usenet client handoff** (L): Protocol type mapping; SABnzbd and NZBGet grab contracts; dispatch at L779-L786. Remaining: Trace Newznab search parsing/auth, client queue/history and actual import; no runtime parity. [pinned source](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/indexers.py#L37-L44) · [pinned source](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/clients.py#L609-L667)
- **F19 — Adaptive release refresh planning** (L): Refresh interval tiers and announced-release watch; 30-minute floor, active 2h, slowing 6h, quiet 24h. Remaining: Verify caller and calendar UI/forecast accuracy separately. [pinned source](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Core/Releases/RefreshSchedule.cs#L34-L105)
- **F39 — Opt-in translation route and engine lifecycle** (L): Authenticated translation routes, availability switch, page requests and admin download/remove/toggle; notice setting in Pass 11. Remaining: OCR/rendering/model rights and actual credit collapse remain unqualified. [pinned source](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/routes/translate.ts#L41-L105)
- **F55 — Sonarr-style v1 REST surface** (C): Status, health, diskspace, configuration GET/PUT/PATCH routes inspected by declaration. Remaining: v3 alias not located in inspected api_v1/main files; verify middleware/contract before claiming compatibility. [pinned source](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/api_v1.py#L1720-L2045)
- **F67 — Selective manga source migration** (L): Chapter number matching, read-through to highest read, bookmarks/date and category copy controlled by migration flags. Remaining: Do not infer all lower chapters read by default; preserve explicit state and verify tracker/replace paths. [pinned source](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/mihon/domain/migration/usecases/MigrateMangaUseCase.kt#L48-L92)
- **F68 — Unified acquisition and authoritative library** (Proposed): Pass 10 documents shared engine queue and requester separation, not a cross-provider unified queue. Remaining: Require independent central identity, durable jobs, adapter reconciliation and verified import across extension/torrent/direct. [proposed contract](EXTENSION-ACQUISITION-CONTRACT.md)

## Proposed prerequisite/selection rules

Required outcomes are not permission for every optional member. F12 extension download requires a working, qualified engine, trust, browse and safe import; F57/F58 need qualified packaging, storage and recoverable state; F69/F70 need identity, coverage, inventory, guarded mutation, recovery and reviewed resolution. The related pending parents describe broader functionality; select minimum needed slices, not entire clusters by dependency implication. Dependencies are provisional relationships, not verified build ordering. F68 remains pending; central architecture must not be called existing upstream parity. Keep individual criteria even when consolidating similar functionality.

Hard gates: permission/ownership, source authorization, edition/language and coverage, file integrity, root identity, no-clobber and durable recovery. Optional enhancement/translation/offline/social features remain independently selectable. Keep physical deletion disabled by default; do not let progress, score or duplicate similarity override safety.

## Next source review queue

1. Fill F55 v3 contract/auth and F15 complete Newznab/client handoff; F19 scheduler/calendar consumer; F39 OCR/render/credit handling and rights; F04 linking/ACL and tracker semantics; F67 migration preservation.
2. Complete required outcome caller and recovery traces from Pass 9, extension trust/auth/backup live schema from Pass 10, settings consumers and key-level UI from Pass 11.
3. Reconcile remaining module dispositions against MODULE-COVERAGE.csv and the pinned trees, including test-source evidence, generated/vendor and license scope. Historical coverage counts are not recalculated by this pass.
4. Document qualification blockers and obtain actual Windows/Unraid and engine fixture results; don't treat selection as implementation approval.

G3/G4/G5 open; G6 partial; G7 not executed; G8 not approved. No claim of exhaustive feature parity.
