# Roadmap and acceptance gates

Proposed sequencing; not release dates or approval.

| Phase | Work | Exit gate |
| --- | --- | --- |
| 0 | Complete source/runtime audit and record the approved initial scope, stack and license | Evidence and acceptance criteria for each initial feature. [Completion ledger](source-audit/COMPLETION-LEDGER.md) |
| 1 | Shared API/UI, auth, config, DB, Windows/Docker smoke builds | Same fixture on both, restart persists |
| 2A | Read-only library scan, identity, metadata, duplicate discovery | Exact duplicates, repacks, aliases, translations and overlaps correctly classified; original bytes untouched |
| 2B | Remote reading from the primary and progress handoff across Android, Windows and Linux | Resume the same chapter on all three clients. Verify progress revision handling on both primary deployments. [Acceptance cases](REMOTE-READING.md#acceptance-cases) |
| 2C | Required physical file organization and duplicate resolution | Verify reviewed rename, move, relink, quarantine and restore. Preserve original bytes and coverage during conflicts, mount loss and interrupted operations. |
| 3 | Required Suwayomi adapter, authorized source download, persistent queue | End-to-end extension fixture Windows and Unraid, retry/idempotency |
| 4 | Selected torrent/Usenet, policy, scheduler, import | Pack coverage, client/path mapping, conflict/failure tests |
| 5 | Optional device uploads and server subscriptions. Selected reader enhancements and offline support | Upload retries create one verified import. Subscribed libraries retain separate identity and credential contexts. |
| 6 | Selected trackers/OPDS/Komga/Kavita/Mihon, migration, notifications | Contract tests, privacy and no sync loops |
| 7 | Optional conversion, repair, discovery, household, EPUB/Kobo, enhancement | Resource/license review and reversible actions |
| 8 | Installer/portable, Docker amd64, Unraid template; arm64 optional | Actual installs, nonroot, backup/rollback and release qualification |

Tests: corrupt archive, traversal, oversized images, decimals/splits, language editions, related title collisions; no rename overwrite; missing mount/locked file/full disk and restart recovery; copy/hardlink on actual target filesystem; auth bootstrap/token revocation/CSRF/user isolation; offline progress conflict; extension package/API compatibility and failure; backup restore and upgrade/rollback. Record exact build, OS, filesystem, test fixture, steps and actual result; CI alone isn't platform qualification.

## Decisions

The approved baseline includes remote reading and progress through a single primary, with optional uploads and subscriptions. Record additional feature choices and detailed deployment modes in the existing register. [Approved baseline](IMPLEMENTATION-DECISIONS.md) · [Feature selection](FEATURE-SELECTION.csv)
