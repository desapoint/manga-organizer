# Pass 14 — module coverage, acquisition and recovery

2026-11-25. Continues [Pass 13](PASS13.md) from `8ee4c42ce06e2980c9d06142adeb513f0e1411ce`. Reconciles all seven pinned trees and adds selected source evidence for 25 files across Manga-arr (KK), Maki (MK) and Suwayomi (SW), including assertions in three test-source files. Zero upstream tests executed. See the [pinned evidence register](PASS14-EVIDENCE.csv).

## Reconciled coverage

| Repo | Tracked paths | Pass 8 baseline | Through Pass 13 | Through Pass 14 | Implementation candidates | Candidates without mapped evidence |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| KK | 1,098 | 45 | 53 | 59 | 164 | 119 |
| KR | 244 | 36 | 40 | 40 | 119 | 88 |
| KV | 4,123 | 42 | 47 | 47 | 2,800 | 2,761 |
| MH | 1,483 | 42 | 51 | 51 | 958 | 920 |
| MK | 3,755 | 65 | 78 | 79 | 2,042 | 1,978 |
| SW | 1,119 | 41 | 55 | 56 | 908 | 863 |
| UC | 1,894 | 58 | 71 | 71 | 725 | 669 |
| **Total** | **13,716** | **329** | **395** | **403** | **7,716** | **7,398** |

The reconciliation carries forward the 329 matched paths from Pass 8, then unions them with exact file citations from the committed audit CSVs and Markdown, including settings and gap evidence. The historical columns use the same pinned tree index for each repo. A file repeated across passes counts once per repo. Seven distinct citations at other revisions were excluded from this pinned-tree count. Every file citation at the seven audit pins matched an indexed path. A mapped path can represent a declaration, an assertion, or a narrow code excerpt. The counts measure durable evidence links. They cannot establish whole-file behavior, test coverage, or runtime compatibility. References without exact pinned file links may be absent from the counts. [Pass 8 baseline](COVERAGE-BASELINE.md)

The [module disposition CSV](PASS14-MODULE-DISPOSITIONS.csv) accounts for all 13,716 paths in 1,235 repo/module/role rows. Each row identifies inventory-only or cited scope, uncited path count, and remaining review. Grouping uses indexed classifications and documented filename/directory heuristics, so module buckets and implementation totals differ from Pass 8's buckets.

| Inventory role | Tracked | Cited | Disposition |
| --- | ---: | ---: | --- |
| Implementation | 7,716 | 318 | Trace behavior, caller, permission, settings consumer and tests. |
| Test or example | 2,563 | 29 | Identify real assertions, examples and execution prerequisites. |
| Asset or other | 1,194 | 21 | Identify purpose, provenance and redistribution terms. |
| Vendored dependency candidate | 601 | 0 | Confirm component boundaries, version and license before any exclusion from logic review. |
| Documentation | 557 | 4 | Check claims against pinned code and shipped releases. |
| Configuration or resource | 458 | 3 | Trace defaults, persistence, permissions and consumers. |
| Generated candidate | 385 | 0 | Identify the generator and inputs, then verify the output contract. Name-based detection is provisional. |
| Build or deployment | 242 | 28 | Trace build inputs and what ships. Run platform qualification separately. |

From the checkout root, regenerate or list exact candidate paths by repo.

```sh
python3 docs/source-audit/reconcile-coverage.py --output docs/source-audit/PASS14-MODULE-DISPOSITIONS.csv
python3 docs/source-audit/reconcile-coverage.py --missing-implementation KK
```

### Review queue by repository

Use the exact path list alongside the module rows. These are examples from the remaining inventory and partial call paths, with follow-up priorities based on the required organization, deduplication and extension outcomes.

| Repo | Next unresolved implementation paths |
| --- | --- |
| KK | Download-client and settings templates, RSS and client error handling, publication helpers and delegated deletion/rescan replay. Extend the REST mutation matrix across import, notification and client integrations, with v1/v3 and browser-session coverage. |
| KR | Frontend API client and settings bindings, volume merge/copy recovery and cancellation, provider contracts and import retry. |
| KV | Kobo sync and device/settings service contracts, entitlement boundaries, scanner validation and mount recovery. |
| MH | Chapter sync and library/reader setting consumers, tracker adapters, edition/state preservation in migration, mobile backup and restore. |
| MK | Frontend settings serialization and connection bindings, rename controller and integration notifications, release calendar UI, and duplicate file recovery. |
| SW | Extension permissions through authentication modes, generated GraphQL and compatibility settings, extension-store persistence and recovery, Android compatibility code provenance, and backup/download runtime. |
| UC | Repair and repair-run helpers, admin and archive settings UI, KOReader sync route, and edition callers and local progress/ACL. |

## Manga-arr acquisition and API

- **RSS and settings.** The RSS task rereads its configured interval before sleeping, independently of the five-minute completion poll. RSS collection polls enabled indexers concurrently and processes results in priority order and uses an empty search for Newznab/Torznab. The downstream worker applies monitored-series matching, blocklist checks and protocol delay, with a global delay fallback. Pending releases record protocol and source fields. Review partial feed failures and promotion to client handoff before qualifying the workflow. [Pinned evidence](PASS14-EVIDENCE.csv)
- **SABnzbd and NZBGet.** Completion discovery polls SABnzbd history and queue by configured owner, joins `Completed` history by `nzo_id` to owned acquisition rows, maps the reported storage path and queues files for review or import. The separate UI cache reports last-success freshness and does not drive import admission. SABnzbd observations are bounded to the requested history/queue windows, and a non-200 result becomes an empty side before absence processing. Qualify pagination and incomplete/error snapshots before treating absence as cleanup authority. The selected completion and UI cache dispatches cover qBittorrent and SABnzbd. Pass 13 established NZBGet submission, and NZBGet completion and import remain unverified. [Pinned evidence](PASS14-EVIDENCE.csv)
- **Mapping and compatibility API.** Remote path mapping takes the first matching string prefix among matching-host and blank-host rows. Fixture coverage should include ambiguous prefixes, Windows paths and root boundaries. Queue skip/retry and history mark-failed, clear and delete delegate to services. The v1 test source asserts API-key enforcement for selected calls, history outcomes, queue retry state and a rejected worker schedule. Complete the v3 mutation and browser-session matrix before claiming wider compatibility. [Pinned evidence](PASS14-EVIDENCE.csv)

## Physical mutation and recovery

- **Manga-arr.** Startup replays rescan operations, volume deletions and loose-chapter cleanup before import publication recovery and producer registration. The selected publication recovery path can publish or compensate, revalidate artifacts before committing the database, and claim post-commit cleanup under a mutation guard. Runtime replay uses bounded batches and backoff. Journal tests include fault injection, process termination, keyset paging and concurrent history assertions. These source findings leave helper-level verification, exclusive ownership and target filesystem behavior open. [Pinned evidence](PASS14-EVIDENCE.csv)
- **Maki.** Rename recomputes the plan and can reject an outdated preview. The file-only import path leaves the series folder in place, and naming cycles can stage under temporary names. The service attempts to reverse completed moves after a database save failure using its in-memory move list. Durable recovery after process loss remains open. The Baozi renumber repair writes chapter data and a completion marker in one database transaction and checks language-specific number collisions. It leaves physical filenames and ComicInfo for a separate operation. [Pinned evidence](PASS14-EVIDENCE.csv)

## Suwayomi extension authorization and settings

- **Resolver and store authorization.** The schema registers auth and permission directive wiring. The auth wrapper supplies trusted caller context to the resolver, and the permissions wrapper delegates to the user permission check. Store add/remove declare store-management permission. Installation, uninstallation and external upload have distinct checks. Explicit update permission still needs a defensive review across auth modes and resolver variants. The REST icon handler separately requires a user. [Pinned evidence](PASS14-EVIDENCE.csv)
- **Artifact trust and configuration.** The store-backed install path resolves package metadata, checks library compatibility, prepares and loads a JAR and registers source instances. These checks establish the observed compatibility path. The executable provenance, signer continuity, isolation and failure recovery remain separate qualification work. The settings mutation distinguishes user compatibility values from permission-gated global writes. The global updater validates and converts registered values into config state flows. Persistence and consumer behavior still need a per-key trace. [Pinned evidence](PASS14-EVIDENCE.csv)

## Gate disposition

G3, G4 and G5 remain open. G6 remains partial, with licensing and exact release/asset scope outstanding. G7 has no executed upstream tests or Windows, Unraid, Android or companion runtime results in this pass. G8 still requires owner selection. The 356 atomic candidates, 126 proposed capability groups and 70 parent decisions retain their existing evidence and status. [Completion ledger](COMPLETION-LEDGER.md)
