# Pass 9 — physical file operations, download admission and interrupted recovery

Date: 2026-11-25. Pass 8 already exists on main at 7ee87e4e418f05d63f744a5e5c20bff910f5ad2d; continue rather than duplicate. Eight pinned source files across all seven references, selected logic reviewed. KR rename was previously described in FILE-ORGANIZATION.md but not mapped in the Pass 8 evidence index. No source copied or application code written. No tests executed. The 356 atomic candidates remain unchanged: these are refinements of ORG/DUP/SAFE requirements, not newly approved features.

[Evidence and fixture crosswalk](PASS9-EVIDENCE.csv) · [Existing file organization requirements](../FILE-ORGANIZATION.md)

## KR — KiranTheRam/mangarr: rename.py

[Pinned source](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/library/rename.py#L44) (L; selected lines 44-112;122-153).

- Observed: Groups file references; skips ambiguous coverage; samefile permits case-only rename; existence check then shutil.move and reference update.
- Limit: Precheck is not atomic admission; no journal in function; samefile may also describe distinct hardlink names.
- Proposed requirement mapping: ORG-03;ORG-04;SAFE-01;SAFE-03.
- Fixture (not run): Two names same inode, target arrives after preview, crash before DB commit.

## MK — OrbitMPGH/Maki: FileLinker.cs

[Pinned source](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Core/Storage/FileLinker.cs#L34) (L; selected lines 34-95).

- Observed: Native hardlink attempt then copy via target.partial; no overwrite final move; cleanup on exception.
- Limit: Fixed partial name and overwrite=true require caller serialization; no strong digest or flush in helper.
- Proposed requirement mapping: ORG-05;DUP-01;SAFE-02.
- Fixture (not run): Concurrent imports to one target, partial collision, cross-device copy, seeding identity.

## MK — OrbitMPGH/Maki: SameVolumeMove.cs

[Pinned source](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Core/Storage/SameVolumeMove.cs#L43) (L; selected lines 43-194).

- Observed: Windows MoveFileEx without replace/copy; Unix renameat2 then link/unlink then check/rename marked Racy; NFS EEXIST same inode check.
- Limit: Racy fallback unacceptable for automatic publication; Landed checks names not object proof; link/unlink rollback needs ownership proof.
- Proposed requirement mapping: ORG-05;SAFE-01;SAFE-03.
- Fixture (not run): Unsupported syscall, occupied target, NFS ambiguous reply, UNC long path, crash between link/unlink.

## UC — AngeloSha/uchiyomi: bulkChapterDelete.ts

[Pinned source](https://github.com/AngeloSha/uchiyomi/blob/dc5d52212cbe7ad1984fbb29d1490e524f886e0c/bff/src/lib/bulkChapterDelete.ts#L148) (L; selected lines 148-195;298-347).

- Observed: Reconcile committed intent by comparing row identity, owned root, containment and stat; keep live row on unreadable/missing parent; skip if source still exists; tombstone after absence; startup row locks.
- Limit: Parent existence alone not mount epoch proof; recovery removes refetch backup despite comment about no destructive work; quarantine preferred.
- Proposed requirement mapping: DUP-07;SAFE-02;SAFE-03.
- Fixture (not run): Missing share but existing empty mountpoint, EACCES, row changed, crash after unlink, refetch backup ownership.

## KK — Kha-kis/manga-arr: rescan_file_recovery.py

[Pinned source](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/rescan_file_recovery.py#L136) (L; selected lines 136-143;627-662;893-955).

- Observed: SHA256 with before/after/current stat identity check; no-replace publication; committed receipt revalidated before discarding carriers; reload durable authority; blocked diagnostics.
- Limit: Only selected paths examined; private claims/store, mount guarantees, caller and tests remain unqualified.
- Proposed requirement mapping: ORG-05;DUP-02;SAFE-01;SAFE-03.
- Fixture (not run): Changed file while hashing, changed published destination, missing receipt, crash around commit, ambiguous allocation.

## KV — Kareadita/Kavita: FolderValidationService.cs

[Pinned source](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Services/Scanner/FolderValidationService.cs#L30) (L; selected lines 30-171).

- Observed: Recent writes filtered by time and parser; deny-writer file open, archive entry/PDF page count checks; deduplicated delayed validation; cap eventually scans without validation.
- Limit: Readiness heuristic not complete decode/hash; scan cap must not authorize destructive reconciliation.
- Proposed requirement mapping: ORG-01;DUP-09;SAFE-02.
- Fixture (not run): Slow write across cap, corrupt but openable archive, unreadable folder, partially copied PDF.

## SW — Suwayomi/Suwayomi-Server: ChaptersFilesProvider.kt

[Pinned source](https://github.com/Suwayomi/Suwayomi-Server/blob/4081b05f02456ec099c85127dfa5253e968d48a7/server/src/main/kotlin/suwayomi/tachidesk/manga/impl/download/fileProvider/ChaptersFilesProvider.kt#L58) (L; selected lines 58-184).

- Observed: Existing count equals requested count short-circuits success; page-prefix matches skip download; ComicInfo then finalize; KOReader hash then cache cleanup.
- Limit: Count and prefix not proof of content/order; inspect specific provider publication and engine queue before trusting handoff.
- Proposed requirement mapping: DUP-03;DUP-09;SAFE-03.
- Fixture (not run): Same count wrong pages, prefix collision, bad cached page, crash during finalize.

## MH — mihonapp/mihon: DownloadPendingDeleter.kt

[Pinned source](https://github.com/mihonapp/mihon/blob/f8e30a86086020aaee1e89a9d015373b1a4ff026/app/src/main/java/eu/kanade/tachiyomi/data/download/DownloadPendingDeleter.kt#L87) (L; selected lines 87-123).

- Observed: Reads pending entries and clears preferences before returning; malformed entries omitted; addUniqueById checks existing list.
- Limit: No durable claim/ack in helper; crash after drain before execution could lose intent; duplicate chapter IDs are not duplicate physical bytes.
- Proposed requirement mapping: DUP-07;SAFE-03.
- Fixture (not run): Crash after drain, malformed queue record, repeated input ID, stale chapter mapping.

## Comparative conclusions and proposed contract

Use KR's conservative coverage-aware preview, MK's native no-replace distinction, KK's durable receipt/identity recovery, UC's explicit uncertain states, KV's delayed scanner admission and SW/MH's download lifecycle as behavioral references, not source reuse. No upstream helper alone proves cross-platform correctness.

| Phase | Proposed invariant | Failure behavior |
|---|---|---|
| Inventory | Read-only by default; record root epoch, object ID, length, change evidence and format | Unknown/unavailable is not empty |
| Match | Byte digest for exact copies; ordered strong page hashes for repacks; perceptual/metadata similarity only candidates | Distinct translation, edition, extra pages or uncertain order kept |
| Preview | Bind source and destination identities, references, coverage, authority and expected storage epoch | Stale plan rejected |
| Stage | Unique operation-owned scratch file, exclusive creation; copy and verify digest and durability; never edit shared inode | Retain original; report incomplete |
| Publish | Atomic no-clobber primitive on qualified storage; explicitly refuse racy fallback | Collision or unsupported capability blocks mutation |
| Commit | Persist receipt, source identity, references and progress remapping; durable transaction with explicit phase | Reload durable state after crash |
| Retire | Only after verified publication and committed references; quarantine reviewed managed copy, preserve client-owned seeding reference | Retain original if any uncertainty |
| Recover | Idempotent, verify mount epoch and receipt; no new destructive action without original authority; durable claims and acknowledgement | Block with diagnostics; no blind retry, purge, or filename-only inference |

These are proposals under existing required F69/F70 and F12, not an owner approval for automatic cleanup. No permanent deletion by default. Do not claim reclaimable space for hardlinks or count partially available files as duplicates. No physical content identity derived from KOReader hash, chapter ID, page count or a path alone.

## Remaining closure work

- Trace KR rename callers/DB commit, MK FileLinker/SameVolumeMove consumers, UC libraryAdmin authorization and backup cleanup, KK identity claim/store/consumer and rescan call graph, KV archive/parser/scanner reconciliation, SW provider-specific finalization and queue, MH pending deletion consumer and persistent acknowledgement.
- Cross-reference full test bodies and add fixtures for Windows NTFS/UNC, actual Unraid storage/mount replacement, hardlink/seeding, cross-device copy, stale plans, interruption at every durable transition, page mismatch and changed references.
- Continue module dispositions and settings-consumer/UI crosswalk, extension trust and versioned API qualification. Coverage baseline is a historical Pass 8 snapshot, not updated to imply full-file review.

G3/G4/G5 remain open; G6 partial; G7 target qualification not run; G8 unapproved. No new licensing, stack or optional feature choice.
