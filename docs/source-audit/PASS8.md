# Pass 8 — coverage reconciliation, source test review, candidate grouping

Date 2026-11-25. Existing pass 7 was already committed at d8a100ed605132aedfcf29564e3faa466e80852b; preserved. This is evidence reconciliation and targeted test-source inspection, not an exhaustive audit or runtime run. No application code.

## Results

- All 13716 indexed paths bucketed by heuristic module/role, 329 distinct paths have mapped durable evidence, 0 unmatched keys. See [coverage baseline](COVERAGE-BASELINE.md). Initially 307 paths mapped from atomic and Pass3–7; then Pass1/2 signal files and 14 newly inspected test sources added. Generated/vendor candidate classification is not exclusion approval.
- [14 test-source observations](PASS8-TEST-EVIDENCE.csv) across seven repositories. Reviewed selected names and assertion lines, not complete test bodies, no execution. Tests exercise decimal collision/no overwrite, mount loss, rename race, DB duplicate row repair, bare mount, fake-engine health, exact acquisition owner, simulated NFS publication/crash, mount parsing, reread, empty queue, H2 migration rerun, malformed backups and chapter number recognition. Fake/injected or parser tests cannot prove actual host qualification.
- All 356 atomic IDs retained; 17 parent mappings corrected after comparison to actual parent matrix, [mapping corrections](PARENT-MAPPING-CORRECTIONS.csv). No owner decision changed. 32 proposed overlap groups cover 122 atomic rows; remaining 234 rows require further consolidation/review. Groups are not one-feature equivalence claims; preserve member-specific acceptance criteria.
- [Parent coverage](PARENT-COVERAGE.csv) maps all 70 parent categories; no atomic rows in 7 categories: F04 Language editions and progress; F15 Newznab Usenet SABnzbd; F19 Adaptive checks estimated release calendar; F39 OCR translation credit collapse; F55 REST and Sonarr v1/v3 compatibility; F67 Mihon mobile reading categories and migration; F68 Single unified download queue and authoritative library. Even populated categories are partial. Existing parent Required decisions: F12, F57, F58, F69, F70; no new approvals.

## Prioritized closure work

| Priority | Audit scope | Evidence required |
|---|---|---|
| 1 | Required F69/F70 file operations | Full mutation and recovery call paths, coverage/identity guards, failure branches, test-source-to-implementation traces and actual Windows/Unraid fixtures |
| 2 | Required F12 extension downloading | Real versioned GraphQL schema, authorization/trust, download completion and retry, backup compatibility, companion deployment and authorized fixture |
| 3 | Required F57/F58 packaging | Pass 7 qualification matrix, exact release artifacts and dependencies, host tests and signed/verified release policy |
| 4 | Remaining functional modules | Settings key/default/validation/consumer/UI/test crosswalk, provider-specific contracts, reader/access/migration verification, no route-only parity claims |
| 5 | Audit consolidation | Resolve every remaining module path or explicit justified exclusion; reconcile atomic overlaps and parent coverage, record user decisions separately |

No runtime fixture executed, no source reuse, no optional feature selection. G1/G2 retain historical status; G3/G4/G5 open; G6 partial; G7 blocked for actual host/runtime; G8 not approved. Pass 8 does not close any open gate.
