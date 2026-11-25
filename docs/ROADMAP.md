# Roadmap and acceptance gates

Proposed sequencing; not release dates or approval.

| Phase | Work | Exit gate |
| --- | --- | --- |
| 0 | Complete source/runtime audit and record the approved initial scope, stack and license | Evidence and acceptance criteria for each initial feature. [Completion ledger](source-audit/COMPLETION-LEDGER.md) |
| 1 | Shared API/UI, auth, config, DB, Windows/Docker smoke builds | Same fixture on both, restart persists |
| 2A | Read-only library scan, identity, metadata, duplicate discovery | Exact duplicates, repacks, aliases, translations and overlaps correctly classified; original bytes untouched |
| 2B | Required physical file organization and duplicate resolution | Preview and reviewed execution; rename/move/relink/quarantine/restore; no overwrite or lost coverage; mount loss, hardlink and crash recovery fixtures pass on Windows and Unraid |
| 3 | Required Suwayomi adapter, authorized source download, persistent queue | End-to-end extension fixture Windows and Unraid, retry/idempotency |
| 4 | Selected torrent/Usenet, policy, scheduler, import | Pack coverage, client/path mapping, conflict/failure tests |
| 5 | Reader, progress, dashboard, offline | Resume across devices, user isolation, prefetch not read |
| 6 | Selected trackers/OPDS/Komga/Kavita/Mihon, migration, notifications | Contract tests, privacy and no sync loops |
| 7 | Optional conversion, repair, discovery, household, EPUB/Kobo, enhancement | Resource/license review and reversible actions |
| 8 | Installer/portable, Docker amd64, Unraid template; arm64 optional | Actual installs, nonroot, backup/rollback and release qualification |

Tests: corrupt archive, traversal, oversized images, decimals/splits, language editions, related title collisions; no rename overwrite; missing mount/locked file/full disk and restart recovery; copy/hardlink on actual target filesystem; auth bootstrap/token revocation/CSRF/user isolation; offline progress conflict; extension package/API compatibility and failure; backup restore and upgrade/rollback. Record exact build, OS, filesystem, test fixture, steps and actual result; CI alone isn't platform qualification.

## Decisions

The owner approved the initial implementation baseline for the five required outcomes, contingent on the audit gates. Record choices for optional features and detailed deployment modes in the existing register. [Approved baseline](IMPLEMENTATION-DECISIONS.md) · [Feature selection](FEATURE-SELECTION.csv)
