# Pass 8 coverage baseline (generated from durable records)

2026-11-25. All seven pinned indexes, PASS3–PASS7 evidence CSVs and 356-row atomic register reconciled. This measures **paths with mapped evidence**, not files completely read, code coverage or test coverage. Pass 1/2 declarations not individually linked in atomic register, narrative-only findings and source/test files not captured in the selected records can be absent. Source roles/module buckets are heuristic, not a decision to exclude assets or docs. All paths without evidence remain unreviewed or unmapped; all mapped paths still require complete behavior/consumer/test review as appropriate.

| Repo | Tracked paths | Mapped evidence | Implementation/UI/schema paths | Mapped in those roles | Test/example paths | Mapped tests/examples |
|---|---:|---:|---:|---:|---:|---:|
| KR | 244 | 31 | 121 | 25 | 101 | 0 |
| MK | 3755 | 63 | 2047 | 53 | 1132 | 0 |
| UC | 1894 | 55 | 806 | 45 | 860 | 0 |
| KK | 1098 | 41 | 165 | 34 | 889 | 0 |
| KV | 4123 | 40 | 3396 | 35 | 177 | 0 |
| SW | 1119 | 39 | 915 | 33 | 101 | 0 |
| MH | 1483 | 38 | 961 | 34 | 11 | 0 |

Total 13716 paths, 307 distinct matched paths, 0 unmatched evidence path keys, 827 module/role buckets. [Module coverage](MODULE-COVERAGE.csv) · [Evidence path index](EVIDENCE-PATH-INDEX.csv). No count indicates completion. Tests/examples include UI stories by heuristic; not executed.

## Unmatched evidence paths

None in selected records.

## Next review queue

Prioritize mandatory organization and duplicate handling, extension trust/download lifecycle, reader state preservation and permissions; then remaining providers/settings, full migration paths, tests, UI/UX, packaging and asset inventory. Use existing tracked-path indexes to expand each module. Mark an explicit reviewed/excluded disposition with reason for every path or group; do not infer behavior from filename. Runtime qualification remains separately blocked until actual target environment is available. No implementation or feature approval.
