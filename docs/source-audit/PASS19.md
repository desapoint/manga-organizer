# Pass 19 — verification of passes 1–18

2026-11-25 Toronto. Reconciled the audit at `9a973741587ffd5e8064054d7190ece49ed350cd`, including the Pass 17 coverage follow-up. The repository history contains the source inventories and reports for all 18 passes. Pass 15 predates the commit carrying Pass 14. Pass 16 already explains their order in Git. [Audit baseline](https://github.com/desapoint/manga-organizer/tree/9a973741587ffd5e8064054d7190ece49ed350cd/docs/source-audit)

| Passes | Verification performed | Result |
|---|---|---|
| 1–2 | Fetched the seven pinned Git revisions. Compared every retained path and blob byte count to `git ls-tree -r -l -z`. Checked uniqueness within each index and checked path/line references for 539 first-pass signals and 2,034 second-pass declarations. | All 13,716 rows match, with zero missing or extra paths and zero byte-count differences. All 2,573 signal and declaration locations resolve. |
| 3–7 | Checked the evidence-register identities, anchors and line ranges against pinned blobs. Independently resolved the seven release tags and their Git ancestry. Compared root license files between each audit pin and release. | All tested references resolve. There are one-line corrections to some historical file lengths and range ends. All seven root license files are byte-identical between the tag and its audit pin. |
| 8–10 | Checked cited test and source paths and reran the coverage baseline. | The retained 329-path Pass 8 baseline is included. Test-source evidence retains its original meaning. |
| 11–12 | Reconciled IDs across the feature, membership, canonical and parent selection registers. | 356 unique atomic rows each belong to one of 126 proposed groups. There are 27 setting groups and 70 parent decisions, five Required and 65 Pending. The atomic evidence levels are 82 L and 274 C, with no T rows. |
| 13–16 | Checked evidence URLs against source and reran the Pass 14 reconciliation. | The Pass 14 output exactly matches the checked-in 1,235-row CSV. Its 403 cited paths include 318 implementation candidates. |
| 17–18 | Executed the separate Pass 17 calculator and extended it to Pass 18. | Pass 17 reproduces 427 cited paths, including 333 implementation candidates. The Pass 18 snapshot is reported below. |

The row-level URL check covered 356 atomic references and 376 explicit pinned URL rows from Passes 3–10 and 13–18. Every checked row matched its repository, commit and path, and every explicit URL anchor was within the blob. The line check counted source lines without an artificial trailing empty line. It identified 240 recorded file lengths and 27 recorded range ends that exceeded the blob by one line. Read those historical ranges through the last line of the blob. This validation covered references, tree inventories and register consistency. [Open implementation review](COMPLETION-LEDGER.md)

Separately verified seven pinned links in the Pass 12 gap register and 54 URL occurrences in each of the two settings crosswalks. Eight distinct settings links end one line beyond the pinned blob, in both copies of the crosswalk. The paths and starting anchors resolve. [Pass 12 gap register](PASS12-GAP-EVIDENCE.csv) · [Settings evidence](SETTINGS-CROSSWALK.csv)

## Exact release comparisons

Changed-path counts use `git diff --find-renames --name-only -z`, counting a detected rename once. The merge-base comparisons show each side of diverged history. The endpoint comparison shows the net differences between the audit and release trees. The API counts in the historical comparison used a cap of 300. [Original release comparisons](RELEASE-COMPARISON.csv)

| Repo | Tag | Pin ahead / behind | Base → pin | Base → release | Release → pin |
|---|---|---:|---:|---:|---:|
| KK | `v1.3.2` | 241 / 0 | 190 | 0 | 190 |
| KR | `v0.2.0` | 79 / 0 | 138 | 0 | 138 |
| KV | `v0.9.1.4` | 71 / 0 | 1,462 | 0 | 1,462 |
| MH | `v0.21.3` | 96 / 50 | 374 | 172 | 256 |
| MK | `v0.38.0` | 0 / 0 | 0 | 0 | 0 |
| SW | `v2.4.2373` | 146 / 7 | 410 | 12 | 408 |
| UC | `v0.109.0` | 5 / 0 | 22 | 0 | 22 |

Root license verification covers the seven repository license texts. Distribution and service rights still depend on the resolved components and assets for the chosen package. [License and asset register](LICENSE-ASSET-REGISTER.csv)

## Coverage through Pass 18

The calculator retains the historical evidence inputs and adds the Pass 18 CSV. Pass 18 adds seven distinct pinned paths, bringing the total to 434. Of 7,716 implementation candidates, 337 have citations and 7,379 have no mapped citation. A citation can represent a selected declaration, test, or section of implementation.

```sh
python3 docs/source-audit/reconcile-coverage-through17.py --through-pass 18
```
