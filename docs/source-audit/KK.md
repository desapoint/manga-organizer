# KK — Kha-kis/manga-arr

Pinned revision: [d9353fe8629b90dfc4e44a1ba7215d7ffd233eef](https://github.com/Kha-kis/manga-arr/tree/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef). Reviewed 2026-11-24 (Toronto).

## Scope

1,098 tracked blob paths enumerated without truncation. Four targeted implementation files inspected at this revision; declaration extracts and selected logic reviewed, not a line-by-line audit of every file. [Full path index](index/KK-files.csv), [machine-extracted settings/routes/symbols](index/KK-signals.csv) from [app/routers/api_v1.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/api_v1.py) (178 signals). Classifications are heuristic. Runtime tests not run.

| Path class | Count |
|---|---:|
| build/deploy | 11 |
| config/resource | 4 |
| documentation | 20 |
| other | 11 |
| source candidate | 164 |
| test candidate | 888 |

Top-level distribution: tests (886), app (164), docs (13), .github (9), scripts (5), .dockerignore (1), .gitignore (1), .gitleaks.toml (1), .mailmap (1), .semgrepignore (1), .trivyignore (1), CHANGELOG.md (1).

## Observations from source
- [acquisition_policy.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/acquisition_policy.py#L15-L86): import authority derives from exact client/protocol/download ID and persisted intent or unanimous explicit history; invalid, duplicate-key, conflicting or missing legacy evidence returns unknown. Never interpret unknown as manual authorization. This is a key data-loss prevention rule.
- [import_plan.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/import_plan.py#L48-L151): lease-lost exception, file plan fields for paths, type, pack, special/range and legacy recheck, plan status/failure; publication admission separated from planning. Inspect staged validation, commit and crash boundaries.
- [file_identity_recovery.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/file_identity_recovery.py#L92-L335): versioned recovery decision/plan with hash, declaration, backup receipt and epoch binding, strict serialized validation. Large subsystem; no claim of stable release completeness or NAS qualification. Prefer tested recovery protocol, not complexity for its own sake.
- [suwayomi_.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/suwayomi_.py#L44-L373): configured client, GraphQL helper, source classification, source lookup and persisted source ID/manga ID/URL linkage, title matching, search, chapters, volume extraction. Need manual review of ambiguous matches and independent provenance per edition/client.
## Additional source-tree leads
Distinct import discovery/lease/staging/publication/commit/execution/workers; file identity claims/store/consumers/recovery, mutation locks; metadata provenance/state/reconcile maps; private file/pack claims, volume assembly/cleanup/deletion, release identity/selections, queue, custom format, delay/language/release profiles and settings. API route index below; use tests and release qualification docs to separate branch code from shipped functionality.
## Design implications
Use explicit authority and persistent idempotency, review/preview, staged imports and conservative storage fences; avoid treating unknown identity as permission. Rich acquisition policy is strongest documented benchmark. Release qualification remains separate from observed branch code.
