# Roadmap and acceptance gates

The table orders the approved scope and records the remaining acceptance gates.

| Phase | Work | Exit gate |
| --- | --- | --- |
| 0 | Complete source/runtime audit and record the approved initial scope, stack and license | Evidence and acceptance criteria for each initial feature. [Completion ledger](source-audit/COMPLETION-LEDGER.md) |
| 1 | Shared API/UI, auth, config, DB, Windows/Docker smoke builds | Same fixture on both, restart persists |
| 2A | Responsive catalog and folder picker, then series grouping, cover previews, content hashes and chapter associations | Group and review folder and title candidates. Relink moved files by verified hash. Distinguish exact copies, physical aliases and different releases of the same chapter. Preserve original files. [Catalog identity and association](CATALOG-ASSOCIATION.md) |
| 2B | Start with CBZ and image ZIP in the shared WebUI, then expand formats in the approved order | Verify right-to-left paging, vertical scrolling and progress handoff across Android, Windows and Linux. [Reader formats and acceptance](REMOTE-READING.md#reader-formats) |
| 2C | Required physical file organization, duplicate resolution and intake moves after verified or manual association | Verify backup and restore before enabling managed changes. Verify no-overwrite, revalidation and recovery across conflicts, mount loss and interrupted operations. Unmatched files remain eligible to read. [Import intake and association](FILE-ORGANIZATION.md#import-intake-and-association) |
| 3 | Required separately managed Suwayomi companion, manual authorized downloads, persistent queue and reviewed import | Exercise a synthetic extension on Windows and Unraid. Verify retries and repeat requests create one verified import with provenance. |
| 4 | Optional device uploads and server subscriptions | Upload retries create one verified import. Subscribed libraries retain separate identity and credential contexts. |
| 5 | Target qualification for the installer, portable builds, Docker and Unraid template | Actual installs, nonroot operation, backup and rollback, and release qualification on the selected targets. |

Tests: corrupt archive, traversal, oversized images, decimals/splits, language editions, related title collisions; no rename overwrite; missing mount/locked file/full disk and restart recovery; copy/hardlink on actual target filesystem; auth bootstrap/token revocation/CSRF/user isolation; offline progress conflict; extension package/API compatibility and failure; backup restore and upgrade/rollback. Record exact build, OS, filesystem, test fixture, steps and actual result; CI alone isn't platform qualification.

## WebUI MVP

1. Configure library and intake roles and browse grouped series and physical files. A folder serving both roles shares one scan and inventory.
2. Show file-backed covers with searchable series browsing. Use `cover.png` or the first readable CBZ or image ZIP page. Preserve files when an image or archive is unavailable.
3. Version SQLite migrations and provide a migration command and verification job. Report live scan stages and counts.
4. Search local files and metadata. Fetch basic manga and doujin metadata, record series, volume and chapter links, and show availability against known total chapters.
5. Read CBZ and image ZIP files right to left and resume on another signed-in browser. Count completed chapters against known totals and retain progress through moves of verified content.
6. Preview and confirm file moves and renames. Preserve content identity, prevent overwrite, and record operation and recovery state.

Build and validate each feature in a stacked pull request based on the preceding checked branch. The owner merges and tests them in order. Downloads and complex metadata scraping follow the MVP. A right-to-left layout is sufficient for the first reader. Managed rename and move workflows retain their storage qualification and recovery gates. [Remote reading](REMOTE-READING.md) · [Managed changes](FILE-ORGANIZATION.md)

## Series detail enhancements

Track the responsive series layout, More info dialog, library coverage and chapter browsing in the dedicated checklist. The checklist links to the interactive design reference and records data dependencies such as the release manifest needed to confirm missing chapters or volumes. [Series detail checklist](SERIES-DETAIL-TODO.md)

## Decisions

The remaining candidates, including scheduled acquisition, torrent and Usenet integrations, advanced duplicate classification, household accounts, offline reading and external reader or tracker integrations, are deferred. [Approved scope](IMPLEMENTATION-DECISIONS.md#owner-feature-selection) · [Feature selection](FEATURE-SELECTION.csv)
