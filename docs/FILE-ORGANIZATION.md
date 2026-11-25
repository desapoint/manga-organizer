# File organization and duplicate management

Status: required owner outcomes F69/F70, detailed implementation choices proposed. User wants the application to physically organize files and find/handle duplicates, not merely display a reading catalog. Both standalone Windows and Unraid WebUI must expose these workflows. No implementation yet.

## 1. Organization workflow

- Add multiple roots (local paths, UNC/SMB on Windows, mounted Docker paths); designate read-only, managed or staging roots. Scan recursively with excludes, symlink/reparse-point rules, progress, cancel/pause/resume, incremental/full scans; report unavailable roots rather than treating them as empty.
- Inventory CBZ/CBR/ZIP/RAR, PDF, EPUB, 7z and loose image folders where supported. Record actual format, path, bytes, timestamps, filesystem identity, archive/page counts, validity, ComicInfo.xml, cover and provenance. Support unidentified and partially matched files without deleting them. Exact supported format/decoder list pending.
- Parse title/aliases/year, chapter, decimal/part, volume, ranges, omnibus, edition, language, scanlation group, special/one-shot and publication type; reconcile metadata provider IDs and embedded metadata with manual corrections and confidence. Manual match/unmatch, batch edit, series split/merge and multi-folder adoption; no title-only merge.
- Configurable root routing by media type/language/edition/series or tag, folder/file templates, chapter/volume naming, padding, optional group/year/edition, sanitize illegal names and handle case-only changes. Preview shows old/new paths, reasons, collisions and affected references; allow per-item selection, override or skip.
- Rename in place, move between folders/roots, copy or hardlink where appropriate, optional consolidation of scattered series folders. Retain original formats by default; format conversion and metadata rewriting separate opt-in operations. Preserve original archives during any optional conversion. Optional CBZ volume assembly/splitting requires coverage proof and separate approval.
- New downloads pass through the same matching/duplicate detection and import planner. Existing files can be organized without downloading anything. Re-scan and update reader paths without losing progress, bookmarks, notes or metadata.
- Audit log, undo/recovery plan, batch history, exportable inventory and exceptions. Proposed screen: Library > Organize with Unmatched, Rename/Move, Conflicts, History. Scheduled scans may be enabled; automatic mutations remain off until explicitly approved.

Example layout (configurable, not mandated):

    Manga/
      Series Title (2020)/
        English/
          Series Title - Vol. 01.cbz
          Series Title - Ch. 012.5.cbz
        French/
          Series Title - Vol. 01.cbz

## 2. Duplicate taxonomy and detection

| Class | Evidence | Default treatment |
|---|---|---|
| Duplicate database rows | Same proven physical path/object, repeated references | Review and relink records; no physical delete |
| Same file via hardlink/alias | Filesystem identity and path relation | Show as linked references; do not count as reclaimable duplicate bytes |
| Byte-identical archive/PDF | Size prefilter, full streaming SHA-256, revalidate before action | High-confidence exact group; suggest verified survivor |
| Repacked same pages | Ordered strong per-page hashes, count and reading order; compare metadata/extras | Review identical content with differing archive metadata; preserve richer metadata |
| Re-encoded/resized same artwork | Optional decoded-image/perceptual comparison with page alignment and thresholds | Similarity candidate only; never auto-remove |
| Duplicate chapter/release | Work+edition+language+chapter/part identity with different sources/quality | Compare releases; may be intentional alternate |
| Overlapping volume/chapter/range | Explicit coverage and page identity, mappings and unique extras | Preserve every unique chapter/page; review only |
| Duplicate series/folder | Provider IDs, title aliases, year, edition and coverage | Manual review and merge/split; no title-only deletion |
| Corrupt/incomplete/mislabeled | Decode/validation failures, missing pages, uncertain mapping | Separate health finding; repair/re-fetch optional, never treat as verified duplicate |

Proposed staged matching: indexed file facts -> size and cheap archive manifest hints -> strong full-byte hash for exact groups -> ordered page hashes for repacks -> optional similarity pass for transformed images -> metadata and coverage matching. Cheap fingerprints (CRC/entry names/sizes) are for candidate generation, not proof for deletion. Hash PDF bytes by streaming, not whole-file memory reads. For loose images use strong per-page hashes rather than name/size alone. Detect shared cover/credits, reordered pages, extra pages, different translations and revised art. Hash caches keyed to file identity, size/change evidence and algorithm version; stale or unreadable fingerprints are unknown, not matching. Bound I/O, CPU, memory and extraction; no archive bombs or symlink escapes.

## 3. Duplicate manager UX and resolution

Proposed dedicated Duplicates screen: scope by library/series/root, filter by class, confidence, language, format, status, size and estimated reclaimable space; group side by side with paths, hashes, page count, resolution/quality, source/group, edition, chapter coverage, file health, timestamps, bookmarks/progress dependencies and seeding/link status. Open sample pages or full visual comparison locally. Show why the match exists and the evidence missing. Candidate totals and actual potential savings distinguish hardlinks, copies and storage pools.

Proposed configurable keep preference: complete/valid first, exact edition/language, coverage, preferred group/source, official/digital release if known, quality/resolution, metadata completeness, desired root/format, then deterministic tie-break. User overrides always win. File size alone is not quality. Never prefer a partial file over a complete one, remove the only good copy, or silently merge distinct translations/editions.

Actions: keep both, ignore/whitelist, select preferred copy, relink database references, merge series records (with collision review), rename/move, quarantine redundant file, restore, or explicitly permanently delete from quarantine. Batch actions and preview supported. Suggested default: scan automatically if enabled, suggest exact-copy resolution, execute only after review, quarantine with configurable retention (no automatic purge by default). Near duplicates, edition differences, coverage overlaps and uncertain matches require manual review even if automated exact-copy handling is later approved.

## 4. Mutation and NAS safety

- Root allowlist and admin authorization; per-file plan with source identity, strong evidence, destination, affected records, coverage before/after, estimated net bytes and rollback; revalidate at execution time against current file, root, locks, mount and references.
- Never overwrite a different file; handle collisions explicitly. Use no-clobber atomic rename where available. Cross-filesystem move: copy to staging, fsync/verify size/hash, publish, update durable references, only then remove/quarantine original with explicit authority. Journal phases and make restart/retry idempotent. Do not assume DB and filesystem are one transaction.
- Missing mount, changed storage epoch, low space, permission failure, locked file, path traversal or ambiguous file identity stops affected mutations. Preserve original and explain failure.
- Preserve hardlink/torrent seeding; never modify in-place shared content. Detect actual filesystem object and link count; unlink only approved managed reference, never touch client-owned download. No cross-share hardlink assumption. Exclude quarantine from scanner and external reader roots; preserve restore manifest and prevent name collision on restore.
- Protect per-user progress, bookmarks, annotations and external IDs. Remap only when content/page mapping proven; otherwise preserve old reference and request review. Enforce access rules in preview, scan, logs and execution.

## 5. Focused upstream source observations

These are targeted static inspections at pinned revisions, not runtime tests, copied code, or measured rankings.

| Reference | Observed implementation | Takeaway / limit |
|---|---|---|
| [KR rename.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/library/rename.py) | Groups chapter references by file; distinguishes named volumes from chapters; skips ambiguous/range coverage, previews collisions and preserves extensions, updates chapter paths/provenance on move. | Conservative rename benchmark. Existing pre-check plus move is not sufficient proof of race-free no-clobber; enforce atomic admission and journal. |
| [MK HealthDuplicates.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Services/HealthDuplicates.cs) | Groups recorded content hashes, keeps linked copies (or an existing oldest record if none linked), marks spare only when kept copy exists; relink target requires same root/folder/hash, unclaimed candidate and different physical identity. | Strong duplicate-review model; revalidate actual bytes and references before mutation. Grouping code alone does not establish hash generation. |
| [MK ChapterFileDuplicateRepairService.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Services/ChapterFileDuplicateRepairService.cs) | Groups duplicate rows by series and path, folds case on Windows, reassigns chapter/health references, invalidates archive cache and removes redundant rows transactionally; one-time startup marker. | Database repair is distinct from deleting physical media. Filesystem case sensitivity should be checked per root, not assumed solely from OS. |
| [UC fingerprint.ts](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/fingerprint.ts) | ZIP/RAR entry name, CRC and uncompressed size sorted and digested; survives compression changes, sensitive to entry rename/metadata; loose-image folders use weaker name/size; PDFs full-file digest; failure returns null. | Efficient candidate index, not proof of exact pages or order. Use strong ordered hashes for destructive resolution; bounded streaming for large PDFs. |
| [KK rename_plan.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/rename_plan.py) | Read-only preview across chapter/volume rows; checks unavailable series path, missing source, occupied target and multiple items targeting same name; records canRename/conflict. | Best batch conflict-planning reference combined with prior exact acquisition/file identity recovery audit; full execution and NAS qualification still pending. |

Pass 3 inspected selected logic/declarations in these areas: MK page hash, file identity, rename; UC page hash, fingerprint job, health files and numbering; KR scanner, matcher, volume merge and publish; KK file identity, mutation lock, volume assembly, journal; KV scanner and mount detection; SW archive/folder providers; MH duplicate candidate, legacy paths and backup restore. See [pass 3 findings](source-audit/PASS3.md). Full mutation/recovery call paths, UI/settings consumers and target-platform fixtures remain to verify. See [source-audit](source-audit/README.md) and [reference registry](../REFERENCES.md).

## 6. Proposed granular requirements

| ID | Capability | Proposed priority |
|---|---|---|
| ORG-01 | Multi-root read-only inventory and incremental scan | Foundation |
| ORG-02 | Metadata parsing, reviewed matching and unknown queue | Foundation |
| ORG-03 | Configurable folder/file naming and root routing | Foundation |
| ORG-04 | Dry-run bulk rename/move and conflict review | Foundation |
| ORG-05 | Safe copy/hardlink/move and durable path/reference update | Foundation |
| ORG-06 | Series merge/split, manual chapter/range mapping | Core |
| ORG-07 | Optional archive conversion/assembly with coverage proof | Optional |
| DUP-01 | Database/path alias and physical file identity | Foundation |
| DUP-02 | Exact byte hash duplicate groups | Foundation |
| DUP-03 | Ordered page content equality for repacks | Foundation |
| DUP-04 | Similarity candidate detection for re-encoded images | Optional |
| DUP-05 | Edition/language/group and overlap classification | Foundation |
| DUP-06 | Side-by-side review, keep preference and exceptions | Foundation |
| DUP-07 | Reviewed relink/quarantine/restore and batch resolution | Foundation |
| DUP-08 | Actual reclaimable-space estimate and report | Core |
| DUP-09 | Pre-import duplicate gate and idempotent reconciliation | Foundation |
| SAFE-01 | No-clobber and stale-plan rejection | Foundation |
| SAFE-02 | Missing mount, storage/permission and seeding protection | Foundation |
| SAFE-03 | Operation journal, recovery and progress preservation | Foundation |

These requirements are proposed breakdowns of the required outcomes, not separate owner approvals. Track individual choices in the future atomic register; do not mark optional conversion or similarity as approved.

## 7. Acceptance fixtures (Windows and actual Unraid)

1. Same file with different name/path; exact duplicate copies grouped; hardlinks not counted as separate storage. Identical file referenced twice in DB can relink without deleting disk data.
2. Same pages in different compression/archives, renamed entries and metadata; ordered content match distinguished from reorder, extras and same-cover-only files.
3. Re-encoded/resized art is only a suggestion; similar chapters and alternate translations never auto-delete.
4. Decimal, split, specials, omnibus and volume/chapter overlaps; no lost unique coverage; wrong series/edition and unknown chapter mappings fail closed.
5. Bulk rename with collision, case-only change, reserved names, long/UNC paths, same-root and cross-filesystem move; target modified after preview blocks operation. Verify metadata/progress and external reader visibility.
6. Protected read-only root, symlink escape, archive bomb, corrupt archive, no free space, file locked, permission denied, unavailable NAS/mount replacement, cancellation/restart mid-import, interrupted cross-filesystem move; no original data lost.
7. Quarantine and restore, restore collision, retention disabled, batch preview and audit export. Seeder-owned hardlinks remain intact; duplicate space estimate reports uncertainty.
8. Repeated scans and imports idempotent; large library background scan bounded and pausable; no unauthorized user can see or mutate unrelated library files.

Implementation remains gated on feature selection, source audit and target-platform testing. 

## Pass 4 safety execution evidence (2026-11-25)

[Pass 4 report](source-audit/PASS4.md) traces Maki hash/identity revalidation and uncancellable DB cleanup after direct deletion, Uchiyomi two-phase temporary rename and best-effort rollback, manga-arr lease/staging/prepared-publication boundary, Kavita watcher cap and active-write detection, and Mihon duplicate SQL (title substring OR nonzero matching tracker ID, not physical duplicate detection). Proposed defaults remain quarantine rather than immediate delete, no-clobber operations even after preview, explicit recovery states and distinct bibliographic vs physical identity. Neither UI confirmation nor watcher completion is proof that the filesystem has not changed. Full call paths and runtime fixtures still pending.
