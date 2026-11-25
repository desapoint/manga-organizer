# Roadmap and acceptance gates

The table orders the approved scope and records the remaining acceptance gates.

| Phase | Work | Exit gate |
| --- | --- | --- |
| 0 | Complete source/runtime audit and record the approved initial scope, stack and license | Evidence and acceptance criteria for each initial feature. [Completion ledger](source-audit/COMPLETION-LEDGER.md) |
| 1 | Shared API/UI, auth, config, DB, Windows/Docker smoke builds | Same fixture on both, restart persists |
| 2A | Read-only library inventory, basic metadata corrections and exact duplicate review | Identify byte-identical files and distinguish copies, hardlink aliases and unknown identities. Preserve original files. |
| 2B | Start with CBZ and image ZIP in the shared WebUI, then expand formats in the approved order | Verify right-to-left paging, vertical scrolling and progress handoff across Android, Windows and Linux. [Reader formats and acceptance](REMOTE-READING.md#reader-formats) |
| 2C | Required physical file organization and duplicate resolution | Verify backup and restore before enabling managed changes. Verify reviewed rename, move, relink, quarantine and restore across conflicts, mount loss and interrupted operations. |
| 3 | Required separately managed Suwayomi companion, manual authorized downloads, persistent queue and reviewed import | Exercise a synthetic extension on Windows and Unraid. Verify retries and repeat requests create one verified import with provenance. |
| 4 | Optional device uploads and server subscriptions | Upload retries create one verified import. Subscribed libraries retain separate identity and credential contexts. |
| 5 | Target qualification for the installer, portable builds, Docker and Unraid template | Actual installs, nonroot operation, backup and rollback, and release qualification on the selected targets. |

Tests: corrupt archive, traversal, oversized images, decimals/splits, language editions, related title collisions; no rename overwrite; missing mount/locked file/full disk and restart recovery; copy/hardlink on actual target filesystem; auth bootstrap/token revocation/CSRF/user isolation; offline progress conflict; extension package/API compatibility and failure; backup restore and upgrade/rollback. Record exact build, OS, filesystem, test fixture, steps and actual result; CI alone isn't platform qualification.

## Decisions

The remaining candidates, including scheduled acquisition, torrent and Usenet integrations, advanced duplicate classification, household accounts, offline reading and external reader or tracker integrations, are deferred. [Approved scope](IMPLEMENTATION-DECISIONS.md#owner-feature-selection) · [Feature selection](FEATURE-SELECTION.csv)
