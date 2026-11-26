# Tools, intake and library navigation

This is the owner-confirmed workflow for the Tools and Import follow-up. Reuse the existing scan, series, duplicate and file-operation services. [Implementation backlog](EXPANDED-BACKLOG.md)

## Navigation and ownership

| Surface | Primary job |
| --- | --- |
| Library | Browse the published scan inventory, covers, linked metadata, chapters and volumes. Keep the current reader entry points. Show eligibility on the server before pagination and counts. |
| Import | Discover and scan intake folders, then review associations, metadata, artwork and destination. Start on **Needs attention**. **All files** shows the full discovered intake inventory, including eligible files and unresolved files. |
| Tools | Provide a dedicated desktop and mobile destination for Duplicates, Versions and alternatives, Associations, Organize and Maintenance. Each module has its own scoped index and filters. Link into the relevant module from Library or Import when useful. Reuse one implementation for each action and its authorization. |

Use the existing workspace navigation and focus conventions. Deep links retain module, root, series and active filters. The inspector stays stable while the pointer highlights a candidate or chapter. Use the current selection and table components so keyboard and mobile workflows remain available.

## Duplicate and version workbench

- **Duplicates** lists candidate groups by series across authorized libraries. Filters apply before pagination and counts. Include library, match class, review status, and file scope. Show exact copies and aliases separately. Attach affected files and the reason each candidate was detected. Preserve navigation back to the series summary and its filters.
- A series detail offers **File duplicates** and **Series overlaps**. File duplicates can be scoped to the selected file and its candidates. Matching-span pairs default to the compact comparison. Compare archive format, image-page counts, ComicInfo, covers, path, accessible page samples and available release attributes. Same-number volumes with unresolved chapter labels can be compared here and show uncertainty. The fast flow keeps the retained file and chosen action visible and still uses the existing guarded preview and confirmation.
- **Series overlaps** opens the full chapter coverage map for the entire series, including partially overlapping volumes and collections. Use the current volume colors, tile-local chapter stripes, chapter inspector, and clickable and hover highlights. Keep the whole-file coverage and loss preview available before removal. [Duplicate evidence and safety](DUPLICATE-AND-TRASH.md)
- **Versions and alternatives** shows only series with multiple releases for the same resolved content span or a reviewable candidate span, including same-number volumes that require verification. Exact byte copies alone belong in Duplicates. Filter versions by language, color, censorship, edition, group and review status. Enable the same release editor for selected candidates and for single or bulk files in Associations or Import even when there is no duplicate. Persist language, censored/uncensored/mixed/unknown, black-and-white/full-color/partial/unknown and other supported fields as independent release facts. Retain evidence and explicit owner overrides. Future acquisition can read the saved fields to request multiple languages or versions under the separately approved acquisition policy. [Release attributes](RELEASE-IDENTITY.md#release-attributes-and-evidence)

## Intake issues and destination

The scanner still owns discovered file rows and content identity. Library reads the last published scan state. A failed or unavailable scan preserves its previous inventory. Import exposes unresolved files even when they are filtered out of Library. Surface nonexclusive issue fields for missing or unconfirmed association, no linked series, missing required metadata and missing cover art. Issue filter selections and counts use the same server query. For a dual-purpose root, use one inventory entry and present the view states separately.

Library shows scanned files even in dual-purpose folders only after a series is confirmed and it has either AniList-linked or owner-entered metadata. A missing cover is an Import attention issue and the item can still appear in Library. Track ComicInfo and file or folder artwork as evidence. A future automatic metadata agent can fill evidence through the same path after its own approval and tests. Use the same confirmed series and metadata check before publishing an intake transfer. Manual metadata is a full route when AniList has no suitable match.

Persist one **Target library** root ID per Import folder. Every eligible item in that folder uses the same target, including mixed-series batches. Show the mapped root and planned relative paths before transfer. Missing, removed or unavailable targets leave the files in intake and identify the configuration issue. A dual-purpose folder mapped to itself needs no cross-root move. The first slice uses reviewed transfers with the current no-clobber previews, permissions, revalidation and recovery journal. Automatic movement remains a later qualified path under the prior intake policy. Defer rules that choose targets by language, version, series or other filters. [Current intake and batch flow](FILE-ORGANIZATION.md#import-intake-and-association)

## Volume navigation

`SeriesPage` currently sets the volume filter when it opens Chapters. Its Volume row’s **View files** branch only switches tabs. The existing file query takes series and search filters without a volume argument. Wire both entry points to a shared selected-volume state or route query. Show the applied filter and a clear action. Apply it server-side before counts and paging, using verified edition membership where available and visibly labeled volume hints for unresolved archives. Use the existing **Read file** entry point for playable volume files. Offer **Read volume** or a release picker directly in Volumes even when chapter assignments are unresolved, and preserve file-level resume. Verified chapter offsets belong to the separate reader mapping work. [Series detail checklist](SERIES-DETAIL-TODO.md)

## Maintenance action menu

Place common actions in the Tools hub with a predictable action menu and per-job result. Support rescanning all or selected configured libraries via the existing queue, with availability, per-root progress and duplicate-job guards. Cache maintenance clears rebuildable artifacts separately from release evidence, owner choices or file and recovery bytes. Report what was cleared and allow the ordinary scan or reader to rebuild it.

Provide separate reset levels for catalog, progress and configuration. The preview lists affected records, retained data and consequences for a fresh scan or reconfiguration. A catalog reset clears and rebuilds the selected inventory and associations while preserving identity references needed by retained progress and recovery records. Progress reset explicitly targets reading state. Configuration reset targets selected editable settings, preserves server identity and credentials, and keeps root references required for recovery even if active settings are reset. Implement from an allowlisted record inventory under a verified backup and exclusive maintenance fence. Refuse or defer while affected operations are active, require dedicated confirmation and make interruption recoverable. Every level preserves media bytes, trash and recovery files and the records needed to restore them. [Backup and recovery](UPGRADE-RECOVERY.md) · [Managed trash](DUPLICATE-AND-TRASH.md)

## Confirmed workflow decisions

| Decision | Status |
| --- | --- |
| Library admission | Confirmed series plus AniList or manual metadata on a scanned file. Missing covers remain an intake issue. |
| Destination routing | One saved target library per Import folder. Filter-based routing follows in a later release. |
| Maintenance reset | Separate previewed catalog, progress and configuration levels. Preserve media and recovery files. |

Build volume navigation and the read-only Tools entry points first. Stage guarded intake and maintenance mutations behind their existing safety work. [Task and test gates](EXPANDED-BACKLOG.md)
