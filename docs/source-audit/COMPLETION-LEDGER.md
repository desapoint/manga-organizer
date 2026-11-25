# Audit completion ledger

Updated 2026-11-25 Toronto. Status **OPEN**. Pass 22 qualified fresh installation of the published Suwayomi MSI on a hosted runner. The owner has since recorded the initial feature selection and deferred the remaining candidates. Target-specific tests and scoped source and licensing review remain open. [Pass 21](PASS21.md) · [Pass 22](PASS22.md) · [Approved scope](../IMPLEMENTATION-DECISIONS.md#owner-feature-selection)

Evidence: I = indexed paths; C = selected source declarations/logic inspected; S = selected test source inspected; R = release note checked; U = outstanding full behavior review. No "complete" status for modules with uninspected consumers, authorization, migrations or runtime tests.

| Repository | Source/route/settings evidence | Required file organization & duplicates | Integrations, release and test evidence | Remaining to close |
|---|---|---|---|---|
| KR | I/C: model, defaults, settings/API, tasks, importer, rename | C/S: scanner, matcher, publish, volume merge, rename tests | R: v0.2.0; partial qBit/Torznab/Kavita/metadata surfaces | U: each provider contract, auth, jobs, image provenance, volume cleanup, frontend bindings, full test suite |
| MK | I/C: controllers, settings, custom stores, upgrade rules, jobs | C/S: hash groups, file row repair, physical identity, rename and perceptual pages | R: v0.38.0; selected integration controllers | U: service consumers, quality evaluator, provider/client contracts, custom repository trust, actual UI/settings, enhancement and model/data licenses |
| UC | I/C: admin, sources, extensions, health, personal, numbering, title extraction | C/S: fingerprints, backfill, page hash, health and preview | R: v0.109.0; extension bridge and reader/progress surface | U: DB/schema, repair and actual renumber mutation, private permission paths, all source/tracker clients, offline/desktop behavior, full tests |
| KK | I/C: policy, profiles, API, import plans, publication/identity and locks | C/S: conflict plan, claims, volume evidence, journal tests | R: v1.3.2; branch differs from qualified tag; Suwayomi/client surfaces | U: full publication/recovery state machine, NAS qualification scope, profile scoring, provider APIs, migration, backup/restore, UI and release diff |
| KV | I/C: reader, settings, plugin, Kobo, library, OPDS and device routes | C/S: mount and scanner guards, scan request dedup tests | R: v0.9.1.4; premium boundaries partly noted | U: parser/metadata and folder validation internals, permissions, file identity, read/write sync, actual formats, plus entitlements, UI, migrations |
| SW | I/C: extension types/store, download manager, config, folder/archive providers | C/S: output/provider tests | R: v2.4.2373; GraphQL declarations and backup validator | U: actual generated GraphQL schema/mutations/auth, installer/trust, extension compatibility, download completion and backup import/export, container/Windows runtime |
| MH | I/C: extension manager/loader, source contract, preferences, backup models/restore | C/S: duplicate candidates, download naming and legacy tests | R: v0.21.3; legacy backup/extension warning | U: SQL duplicate matching, reader/preference consumers, backup version fixtures, tracker adapters, update policy, Android integration/runtime |

## Exit gates and remaining execution

| Gate | State | Required evidence to close |
|---|---|---|
| G1 All seven pinned tracked trees indexed | Done | Pass 19 matched each of 13,716 indexed paths and its byte count to Git. |
| G2 Prior filename-only candidates resolved | Done | All 23 historical candidates were resolved in Pass 3. Pass 19 verified the 356 retained atomic paths. |
| G3 Complete feature and setting audit | Open | Finish route/schema/settings/UI, defaults, consumers, authorization and migration review, or record a module exclusion with its reasoning. Include the progress authority and client import path in the required-outcome review. The Pass 20 coverage command reports the remaining path inventory. [Remote reading requirements](../REMOTE-READING.md) |
| G4 Organization and duplicate handling | Partial | Linux tests substantiate selected safety and recovery behavior, and hosted Windows passed basic NTFS fixtures. Complete mutation, content/edition identity and recovery coverage on target NTFS/UNC and Unraid pool/share storage. [Acceptance fixtures](../FILE-ORGANIZATION.md) |
| G5 Extension compatibility | Partial | Published `v2.4.2373` downloaded via synthetic JAR and APK fixtures. Both audited Mihon revisions decoded the synthetic backup on the JVM. Qualify ambiguous acknowledgement handling, store approval, additional format/version cases, scoped roles, Android restore and verified organizer import. [Pass 22](PASS22.md) |
| G6 Release and licensing | Partial | All tags and root licenses checked against Git. Verify changed behavior for each selected capability, exact distribution manifests and transitive SBOMs, redistribution rights for chosen assets/dependencies, and external service entitlements. |
| G7 Runtime verification | Partial | Hosted Windows covers the upstream portable and checksum-verified MSI fixtures, and Docker covers selected storage behavior. Finish Windows client behavior, actual Unraid pool/share and mount tests, and Android device interoperability. Live reading handoff and optional upload and subscription cases are product acceptance tests. [Qualification matrix](DEPLOYMENT-QUALIFICATION.md) · [Remote reading acceptance cases](../REMOTE-READING.md#acceptance-cases) |
| G8 Owner selection and implementation | Selection complete for the current scope. Implementation underway. | The owner approved the staged reader formats, single-owner WebUI with progress handoff and core library and manual Suwayomi workflow. Device uploads and server subscriptions are optional. The remaining candidates are deferred. The evidence and platform gates still apply to dependent capabilities and releases. [Approved scope](../IMPLEMENTATION-DECISIONS.md#owner-feature-selection) · [Selection register](../FEATURE-SELECTION.csv) |

No destructive automation is approved. Do not mark gates G3–G8 complete based on file counts or test names. An exhaustive source audit requires further module-level inspection; report what was actually examined rather than claiming parity. 

## Pass 4 progress

[Behavior and release contracts](PASS4.md) records partial source-to-UI/consumer traces for each repository, safety execution paths, GraphQL permission/version differences and all seven release comparisons. G3/G4/G5 remain open; remaining full settings inventory, provider/client/tracker contracts, migrations/auth/UI/test coverage, actual schema snapshots, mutation recovery and target runtime tests. G6 improved but license and release-specific behavior not yet closed. No new owner approvals.

## Pass 5 progress

[Provider and integration audit](PASS5.md) adds pinned provider/source/indexer/client/tracker evidence and [integration inventory](INTEGRATION-INVENTORY.csv). G3/G5 improved but remain open: trace each concrete provider and per-user/role UI, defaults and validation; verify authentication, token refresh, rate limits, status mapping, list completeness, licensing and versioned contract tests. G4 storage protections retained. G6/G7 still open/blocked for release-specific behavior, license and runtime. G8 not approved.

## Pass 6 progress

[Reader and database audit](PASS6.md) and [proposed reader/data contracts](READER-DATA-CONTRACTS.md) extend G3/G4/G5 with reader prefs/UI, progress conflicts, private state, schema/entity and selected migrations across all seven. Still open: complete settings-to-consumer/renderer/ACL traces, relationship cascade and migration histories, provider-specific contracts and full runtime fixture results. G6 license/dependency/packaging audit next; G7 target qualification still not executed; G8 no approval. Do not infer full data safety from schema or declarations.

## Pass 7 progress

[License and deployment audit](PASS7.md) verifies all seven root license texts and selected build/manifests, separate asset rights and target test plan. G6 remains partial: exact transitive SBOM, license scope, source headers, model/dataset/extension terms, companion image repository and release-specific verification pending. G7 no actual target tests; [qualification matrix](DEPLOYMENT-QUALIFICATION.md) all not run. G3/G4/G5 still open; G8 no owner approval. Do not treat this as release certification.

## Pass 8 progress

[Coverage reconciliation and test-source review](PASS8.md) maps durable evidence to pinned trees; 14 additional test sources, parent correction log and candidate overlap groups. G3/G4/G5 remain open: explicit module dispositions, complete settings/consumer/test traces, mutation/recovery, actual schema/runtime; G6 partial, G7 blocked for host tests, G8 not approved. No test or deployment certification from source assertions. Use [coverage baseline](COVERAGE-BASELINE.md) for outstanding gaps rather than candidate growth.

## Pass 9 progress

[Physical operations and recovery](PASS9.md) reviews selected logic in eight pinned files across seven references; [evidence and fixture crosswalk](PASS9-EVIDENCE.csv) records limits and proposed ORG/DUP/SAFE tests. G4 improved but open: complete caller/authorization/identity/mount and recovery traces, actual host fixtures. G3/G5 open; G6 partial; G7 not executed; G8 not approved. No atomic count change, no automatic deletion approval. Pass 8 baseline retained as historical snapshot.

## Pass 10 progress

[Extension and acquisition audit](PASS10.md) and [adapter contract](EXTENSION-ACQUISITION-CONTRACT.md) add selected pinned evidence from 20 SW/MH files, client schema field comparison, trust/requester/queue/recovery and admission requirements. G5 improved but open: actual generated schema, auth/loader/signature and role/backup fixtures, companion runtime and version qualification. G3/G4 open; G6 partial; G7 not executed; G8 not approved. 356 atomic candidates unchanged; no code or automatic deletion approval.

## Pass 11 progress

[Settings and profile audit](PASS11.md) and [settings crosswalk](SETTINGS-CROSSWALK.csv) record 27 grouped observations from 27 distinct cited files across all seven references, including scope/default/validation/storage and selected actual consumers. G3 remains open: complete per-key registry, settings UI/consumers/migrations/role/test trace; remaining profile, integration and module dispositions. G4/G5 open, G6 partial, G7 not executed, G8 not approved. 356 atomic candidates retained; no implementation or automatic mutation approval.

## Pass 12 progress

[Consolidation and selection inventory](PASS12.md) reconciles all 356 atomic candidates once into 126 proposed canonical groups, all 70 parents and 27 settings groups; six previously unmapped parents have supplemental pinned evidence, F68 remains proposed-only. Original registers/decisions unchanged; five required outcomes, 65 pending. G3/G4/G5 remain open; G6 partial; G7 not executed; G8 not approved. Next close remaining source/consumer/auth/runtime gaps; no implementation or automatic mutation approval.

## Pass 13 progress

[Targeted evidence-gap review](PASS13.md) and [pinned evidence](PASS13-EVIDENCE.csv) add 22 selected files across five references, including six test-source files (not executed). F55 v3 alias located and registered, selected compatibility test assertions inspected; six other gap areas strengthened, F68 still proposed-only. G3/G4/G5 open; G6 partial; G7 not executed; G8 not approved. Existing 356 atomic candidates, 126 proposed groups, 70 parent decisions unchanged; no implementation or destructive automation approval.

## Pass 14 progress

[Coverage reconciliation and focused implementation traces](PASS14.md) account for all 13,716 paths across seven pinned trees, with 395 evidence paths through Pass 13 and 403 through Pass 14. The [module dispositions](PASS14-MODULE-DISPOSITIONS.csv) show remaining review per repo/module/role. [Pinned evidence](PASS14-EVIDENCE.csv) covers SABnzbd completion and separate NZBGet submission evidence, selected compatibility API mutations, Manga-arr journal recovery, Maki rename and Suwayomi authorization/settings. Three test-source files inspected, zero upstream tests executed. G3/G4/G5 remain open, G6 partial, G7 unexecuted and G8 pending owner selection.

## Pass 15 progress

[Destructive-operation and extension authorization review](PASS15.md) cites 13 pinned files across seven references, 8 test-source files, and [13 proposed qualification fixtures](PASS15-QUALIFICATION.csv) (not run). User reported Pass 14 complete, but no Pass 14 record was accessible on the visible repository branches when reviewed; numbering preserved, no Pass 14 contents assumed. [Evidence](PASS15-EVIDENCE.csv). G3/G4/G5 open; G6 partial; G7 not executed; G8 not approved. Existing 356 atomic candidates and 126 proposed groups unchanged; no application implementation or automatic destructive approval.

## Pass 16 progress

[Acquisition and configuration lifecycle](PASS16.md) reconciles now-accessible Pass 14 with Pass 15. Historical Pass 14 coverage (403 distinct cited paths, 7,398 implementation candidates without citations) retained; no later total asserted. [Evidence](PASS16-EVIDENCE.csv) cites 17 selected pinned files across all seven references, 5 test-source files (not executed); [qualification](PASS16-QUALIFICATION.csv) adds 10 proposed scenarios. NZBGet submission established in prior passes, completion/import still open; uncertainty and requester/owner isolation explicit. G3/G4/G5 open, G6 partial, G7 not executed, G8 pending. Original 356 atomic candidates, 126 groups and 70 parent decisions unchanged. No code implementation or destructive approval.

## Pass 17 progress

[Integration and settings consumer review](PASS17.md) adds 12 selected pinned files across KK/KR/KV, one test-source body not run, [15 settings trace rows](PASS17-SETTINGS-TRACE.csv) and [9 new qualification scenarios](PASS17-QUALIFICATION.csv). [Evidence](PASS17-EVIDENCE.csv). NZBGet completion/status not supported by inspected dispatcher/cache; no full adapter claim. Client retirement and Kobo cursor isolation require qualification. G3/G4/G5 open; G6 partial; G7 not executed; G8 pending. Existing selection and candidate counts unchanged. No application implementation or destructive approval.

### Pass 17 coverage reconciliation

[Coverage reconciliation](PASS17-COVERAGE.md) and [delta](PASS17-COVERAGE-DELTA.csv) check all 42 Pass 15–17 evidence paths against previous citations and pinned indexes: 24 new and 18 previously mapped. Historical 403 distinct paths increased to 427; 333 of 7,716 implementation candidates have citations, 7,383 remain without mapped evidence. Original historical snapshot unchanged. Versioned calculator provided, Python execution pending. Coverage is not complete file inspection or passing tests.

## Pass 18 progress

[Deployment and restore review](PASS18.md) cites 18 pinned files across all seven references, two test-source files (not executed), [eight artifact dispositions](PASS18-ARTIFACTS.csv) and [ten proposed qualification scenarios](PASS18-QUALIFICATION.csv). [Evidence](PASS18-EVIDENCE.csv). Windows install guards, Docker user mapping and embedded DB startup, partial restore/rollback boundaries, dependency hash and mutable launcher evidence recorded. G3/G4/G5 open; G6 partial; G7 not run; G8 pending. Pass 17 coverage historical, no new cumulative total; original 356 candidates, 126 groups and 70 decisions unchanged. No implementation, redistribution or destructive approval.

## Pass 19 verification

Verified all seven trees, all seven release tags, 732 structured pinned citations and the 70/356/126 selection reconciliation. Reproduced the Pass 14 and 17 coverage snapshots and calculated 434 distinct cited paths through Pass 18. Recorded the historical one-line count and range corrections and exact release diff counts. [Verification record](PASS19.md)

## Pass 20 results

Ran the available Linux checks for organization, duplicate handling, authorization, the Suwayomi download and backup contract, and artifact verification. The targeted suites had 861 passing tests across five upstreams. Confirmed the `v2.4.2373` empty-queue behavior in two published artifacts and recorded a separate Manga-arr test setup failure. [Source evidence](PASS20-EVIDENCE.csv) · [Runtime and artifact results](PASS20.md)

## Pass 21 results

Both hosted jobs passed. Windows Server 2025 exercised the portable Suwayomi ZIP, chapter download and NTFS checks. Ubuntu Docker exercised separate configuration and media volumes, UID:GID `99:100` and restart persistence. The local Linux Docker replay also passed. Both hosted jobs observed a GraphQL timeout after successful file creation. Windows installer, actual Unraid pool/share and Android/Mihon fixture outcomes are still pending. [Qualification record](PASS21.md) · [GitHub Actions run](https://github.com/desapoint/manga-organizer/actions/runs/45664698558)

## Pass 22 results

The latest hosted run passed the checksum-verified upstream Windows MSI install, download and uninstall and both Mihon wire-codec tests. The external synthetic APK passed the local Docker download and restart checks. The owner approved the initial baseline contingent on the audit gates. Local Windows client and Unraid storage behavior, Android device restore, scoped module review and applicable distribution obligations remain open. [Pass 22](PASS22.md) · [Hosted run](https://github.com/desapoint/manga-organizer/actions/runs/45668061469)
