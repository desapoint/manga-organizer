# Pass 8 coverage baseline — reconciled

2026-11-25. All seven pinned indexes, Pass 1/2 signal and declaration CSVs, PASS3–PASS7 evidence CSVs, 356-row atomic register and 14 Pass 8 test sources reconciled. **Paths with durable evidence is not files completely read, code coverage or test coverage.** Narrative-only evidence not mapped can be absent. Roles/module buckets are heuristic; vendor/generated candidates still need explicit disposition and license inventory, not automatic exclusion. UI examples counted with tests; no tests executed.

| Repo | Tracked paths | Mapped evidence | Implementation/UI/schema paths | Mapped in those roles | Test/example paths | Mapped tests/examples | Vendor/generated candidates |
|---|---:|---:|---:|---:|---:|---:|---:|
| KR | 244 | 36 | 121 | 28 | 101 | 2 | 0 |
| MK | 3755 | 65 | 1891 | 53 | 1132 | 2 | 156 |
| UC | 1894 | 58 | 806 | 46 | 860 | 2 | 0 |
| KK | 1098 | 45 | 163 | 36 | 288 | 2 | 603 |
| KV | 4123 | 42 | 3167 | 35 | 177 | 2 | 229 |
| SW | 1119 | 41 | 915 | 33 | 101 | 2 | 0 |
| MH | 1483 | 42 | 961 | 36 | 11 | 2 | 0 |

Total 13716 tracked paths, 329 distinct matched paths, 0 unmatched evidence path keys, 829 module/role buckets. The initial pass-8 baseline mapped 307 paths; earlier declarations and targeted tests were added and deduplicated. [Module coverage](MODULE-COVERAGE.csv) · [Evidence path index](EVIDENCE-PATH-INDEX.csv) · [Test evidence](PASS8-TEST-EVIDENCE.csv). No count indicates completion.

## Unmatched evidence paths

None in selected records.

## Review queue

Required organization, duplicate handling, extension trust/download lifecycle, reader state preservation/permissions; then remaining providers/settings, full migration paths, tests, UI/UX, packaging/asset inventory. Explicit review/exclusion disposition with reason per path/group. Full tests remain separate; no implementation or owner approval.
