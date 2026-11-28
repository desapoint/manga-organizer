# File organization and duplicate management

Physical organization and duplicate handling are required outcomes F69/F70 for standalone Windows and the Unraid WebUI. The first implementation builds the read-only inventory described below.

The current implementation includes exact-copy review, reviewed moves and recoverable quarantine. The expanded owner selection adds release-aware comparison and managed trash behind storage qualification, backup and restore gates. Unattended deletion of active library files, automatic repair and seeding integration remain outside this scope. [Approved scope](IMPLEMENTATION-DECISIONS.md#owner-feature-selection) · [Expanded duplicate and trash specification](DUPLICATE-AND-TRASH.md)

## Read-only catalog

The signed-in owner can choose library, import intake, or both for each folder in **Library settings**. A dual-purpose folder appears in both views and shares a single scan, inventory and root ID. Storage permissions for managed changes remain a separate decision. Settings are stored in SQLite and apply without a restart. A starting folder from first launch or the existing environment configuration starts in the library. Removing a configured folder removes it from the active catalog while preserving the files.

The owner can start an inventory scan and browse the results in the WebUI. The scan records relative paths, filename extensions, sizes and modification times for CBZ, CBR, CB7, ZIP, RAR, 7z, PDF and EPUB files. Each scan reads supported regular files to calculate a full SHA-256 digest. The catalog stores one content ID per digest and keeps each indexed path separately across libraries and intake. [Catalog identity](CATALOG-ASSOCIATION.md#paths-hashes-and-associations)

Rescan a folder after upgrading to assign content IDs to its existing entries. Rescan both affected folders after a move between roots. A file whose hash could not be verified stays in the inventory with its content identity marked unavailable. The WebUI shows when matching bytes have multiple indexed locations. A location count can include an offline folder's last inventory and aliases of the same physical file. Physical identity, format validation, loose image folders and chapter matching have separate acceptance checks.

Scans run one at a time. The server skips symbolic links and Windows reparse points, and publishes a successful full inventory as one database transaction. An unavailable root, failed scan or interrupted scan preserves the prior inventory. If an inventory with files suddenly scans empty, the WebUI reports it for review and keeps the prior entries. The next successful scan with files replaces those entries. Mounted media can remain read-only in Docker. See the [configuration instructions](../README.md#configure-the-read-only-library).

During a scan, the WebUI shows files discovered, bytes read while verifying hashes, groups checked for covers and the publishing stage. Both views of a dual-purpose folder observe the same progress. The last published inventory remains available until the new scan completes.

**Quick scan** walks the entire folder and reuses saved hashes and scanned chapter coverage when file identity, size, modification time and filesystem change time agree. Local NTFS on Windows and ext-family, XFS, Btrfs and tmpfs mounts on Linux can supply this evidence. Files on other mounts are read and hashed, including uncertainty on network and pooled shares. **Full verification** reads every media file and refreshes the scan evidence. The scanner reuses matching archive artwork and checks local folder artwork for additions, changes and removals. The first scan after upgrading from a version without incremental scanning hashes previously indexed files once to record the filesystem evidence for their saved hashes. After that scan completes, Quick scan can reuse the evidence for unchanged files. Each completed scan reports files hashed, files reused, bytes read for hashing and duration. Reviewed file operations verify content independently.

Pause saves the scan counters, completed file checks with reliable filesystem evidence, and the file list once discovery finishes. Resume uses the saved list, revalidates the checks, and rebuilds artwork from the current cache and folder contents. The pause survives a server restart. If discovery was still in progress, Resume starts that folder walk again. Stop saves reusable checks and discards the plan. The next scan discovers the folder from scratch. A Quick scan may reuse valid saved checks. A new Full verification rereads the files. Pausing or stopping leaves the last published inventory available. AniList lookups continue in the background after publication and still require confirmation.

## Import intake and association

When a reviewed move would collide with an existing release, allow the owner to keep both using a numbered destination visible in the preview. Preserve each chapter or language variant as a separate file. Allow a reviewed move to bring along source folder artwork, retaining distinct destination cover images with names such as `cover-1.jpg` and `cover-2.jpg`. The planned paths and content must be revalidated at confirmation.

An import intake folder is inventoried in its own WebUI view. The expanded workflow requires a confirmed series, AniList or owner-entered metadata, and a valid mapped target library. The first slice previews and confirms intake transfers. Previously selected unattended movement remains a later path behind qualification and the same admission rules. Ambiguous or changed sources, unqualified storage and destination conflicts keep the file in intake. [Tools and intake](TOOLS-AND-INTAKE.md#intake-issues-and-destination)

The reader must allow eligible files to open from intake or a library when their format is supported, including files with no metadata association. File movement uses the existing verified backup and restore, storage qualification, no-overwrite, revalidation and journal requirements. The current intake inventory provides discovery and configuration. The association and managed movement workflow is scheduled with the organizer's metadata and import operations. [Approved scope](IMPLEMENTATION-DECISIONS.md#owner-feature-selection) · [Reader formats](REMOTE-READING.md#reader-formats)

Group files by folder and normalized title, then allow series assignment before chapter coverage is complete. Store paths separately from verified content hashes and link content to every chapter it covers. Different hashes associated with the same chapter appear together for owner review. Intake scan cover previews use `cover.png` or a supported first page. [Catalog identity and association](CATALOG-ASSOCIATION.md)

## Reviewed moves and renames

Folders start read-only. Enable **Allow reviewed file moves and renames** separately from the library and import roles for each writable folder you want to manage. Scan the source and open **Move or rename** on a verified file. Choose the destination folder and a relative path, preview the source and destination, then confirm the change. The same folder can serve as source and destination for an in-place rename or reorganization.

The server revalidates the file digest and prepares a verified recovery copy before moving the source. It verifies a staged copy on the destination storage and publishes it without overwriting an existing path. The catalog updates the file location while preserving content identity, chapter coverage and reading progress. An archive cover can follow the file immediately, and existing folder artwork takes precedence. Recovery material uses additional storage in the server data directory and hidden recovery files on the media root.

Choose **Keep both when the destination exists** to preview an unused name ending in `[Variant 1]` or the next available number. Choose **Copy folder covers** to include artwork from a source folder that contains one indexed series. The preview lists each cover's destination. Distinct artwork uses `cover-1`, `cover-2`, and later numbers when the destination already has artwork. Originals stay in the source folder, and copied covers stay at the destination after restoring the manga file.

Use **File operation history** in Tools → Organize to inspect results and explicitly restore an original path. Restores recheck content and protect occupied paths. An interrupted operation is marked for review at restart. Docker mounts must be writable for the folders you opt into management. [Installation](INSTALL.md)

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

The series coverage map, file-scoped comparator, compact archive evidence and reviewed batch quarantine are present. The expanded workflow adds a library-wide unresolved queue, multi-label findings, persistent alternatives and reasons, sample pages and release-aware filters. [Workflow and acceptance](DUPLICATE-AND-TRASH.md#findings-and-persistent-alternatives)

Show reliable identity, integrity and retained coverage when recommending a keep choice. Expose language, edition, officiality, group and quality evidence. Keep owner decisions explicit. The separate reader policy governs automatic selection for reading. [Release-selection policy](READING-CONTINUITY.md#release-selection)

The approved trash lifecycle includes a configurable 30-day default for deliberate trash entries, protected and extended retention, batch restore and confirmed permanent purge or emptying. Preview and revalidate each change. Legacy quarantine and unresolved findings do not qualify for expiration without explicit admission. See the lifecycle and its acceptance criteria for exact transitions and storage accounting. [Managed trash](DUPLICATE-AND-TRASH.md#trash-records-and-lifecycle)

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
7. Rehearse current quarantine restore and collisions. Qualify managed trash, retention disabled and enabled, explicit purge and the restore/purge race separately. Keep seeder-owned hardlinks intact and report uncertainty in space estimates. [Expanded storage cases](EXPANDED-ACCEPTANCE.md)
8. Repeated scans and imports idempotent; large library background scan bounded and pausable; no unauthorized user can see or mutate unrelated library files.

Media changes and release qualification remain gated on feature selection, source review and target-platform testing.

## Pass 4 safety execution evidence (2026-11-25)

[Pass 4 report](source-audit/PASS4.md) traces Maki hash/identity revalidation and uncancellable DB cleanup after direct deletion, Uchiyomi two-phase temporary rename and best-effort rollback, manga-arr lease/staging/prepared-publication boundary, Kavita watcher cap and active-write detection, and Mihon duplicate SQL (title substring OR nonzero matching tracker ID, not physical duplicate detection). Proposed defaults remain quarantine rather than immediate delete, no-clobber operations even after preview, explicit recovery states and distinct bibliographic vs physical identity. Neither UI confirmation nor watcher completion is proof that the filesystem has not changed. Full call paths and runtime fixtures still pending.

## Pass 6 — logical reading anchors

See [reader/data contracts](source-audit/READER-DATA-CONTRACTS.md). Preserve stable logical work/edition/chapter IDs independent of path and retain user-specific resume/bookmarks/history. Original page anchors and occurrence/order are distinct from presentation splits/joins/crops; exact repacks may be mapped only with verified evidence. Changed or ambiguous content requires review, preserving unresolved anchors and old provenance. Do not cascade-delete reader state on physical file removal or merge private resources without ACL review. No migration or runtime fixture executed.
