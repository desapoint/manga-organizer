# Pass 17 — evidence coverage reconciliation

Snapshot: 5647bb3779877c0f817fe450c225c8bdf19ad0fe. This extends the committed Pass 14 historical coverage rather than replacing it. Pass 14 records 13,716 indexed paths, 403 mapped pinned paths and 318 implementation candidates with evidence. For the 42 evidence rows in Passes 15–17, compared each path against earlier pinned citations and the retained 329-path baseline, using the same source scope as the historical calculator (55 historical audit Markdown/CSV files, 11 root/docs Markdown/CSV files and PASS14-EVIDENCE.csv). Checked all 42 exact URLs against the seven pinned indexes and matched the indexed role. Eighteen were already cited and 24 were new. No new path belongs to a generated/vendor override bucket. Counts below carry forward the historical Pass 14 totals and add the verified new paths; they do not claim an independent full recalculation of all older citations.

| Repo | Tracked | Through 14 | Added 15 | Added 16 | Added 17 | Through 17 | Implementation cited | Implementation uncited |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| KK | 1098 | 59 | 1 | 3 | 0 | 63 | 47 | 117 |
| KR | 244 | 40 | 1 | 2 | 0 | 43 | 32 | 87 |
| KV | 4123 | 47 | 1 | 1 | 3 | 52 | 43 | 2757 |
| MH | 1483 | 51 | 1 | 0 | 0 | 52 | 39 | 919 |
| MK | 3755 | 79 | 1 | 4 | 0 | 84 | 68 | 1974 |
| SW | 1119 | 56 | 1 | 1 | 0 | 58 | 46 | 862 |
| UC | 1894 | 71 | 2 | 2 | 0 | 75 | 58 | 667 |
| TOTAL | 13716 | 403 | 8 | 13 | 3 | 427 | 333 | 7383 |

- 403 → 411 → 424 → 427 distinct pinned evidence paths.
- 318 → 333 implementation paths with mapped evidence; 7,383 of 7,716 implementation candidates still lack mapped citations.
- 15 new implementation paths and nine new test-candidate paths; tests not executed.
- [Counts](PASS17-COVERAGE.csv) and [42-row evidence disposition](PASS17-COVERAGE-DELTA.csv).

The separately versioned calculator extends the historical script's evidence input to a chosen cutoff (14–17). The incremental reconciliation was performed using exact-path searches and JavaScript index comparisons; the Python calculator itself was not executed here. To regenerate module dispositions from the checkout:

```sh
python3 docs/source-audit/reconcile-coverage-through17.py --through-pass 17 --output /tmp/pass17-modules.csv
python3 docs/source-audit/reconcile-coverage-through17.py --through-pass 17 --missing-implementation KK
```

The original Pass 14 calculator and module disposition snapshot are unchanged. A cited path does not mean every line, setting, caller or feature is inspected. No runtime coverage or qualification claimed; G3/G4/G5 remain open, G6 partial, G7 not run and G8 pending.
