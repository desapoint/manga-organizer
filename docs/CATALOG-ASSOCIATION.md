# Catalog identity and association

These are owner-selected requirements for the organizer and reader. Apply the workflow to both the library and import intake. [File organization](FILE-ORGANIZATION.md) · [Reader formats](REMOTE-READING.md#reader-formats)

## Group files into series

Library and import inventories show path-based series candidates as expandable groups. Grouping happens before pagination. A group can be suggested even when its chapter or volume coverage is unresolved. The inventory retains paths and content IDs so the owner can inspect the original entries.

- Use the containing folder as the series candidate when filenames contain only chapter numbers, volume numbers, ranges or numbering. Parse decimal chapters and parts without losing precision. A folder name like `Volume 02` inherits a series candidate from its parent. A generic intake folder with files from several series leaves ambiguous entries for review.
- A filename beginning with explicit volume or chapter numbering and a separated subtitle also uses the closest series folder. Skip numbered volume and chapter subfolders when looking for that series. The subtitle and release annotations stay visible in the original filename.
- Look for the same or a closely matching title across folders. For title comparison, normalize case, spacing and punctuation and parse recognized release group tags and status words such as `ongoing`. Retain the original names and parsed provenance. Preserve meaningful numbers, subtitles, edition and language evidence.
- Recompute suggested groups on the next scan. Recognize explicit numbering such as `v01`, `Chapter 107` and `Tome 01` before release annotations. A bare chapter number can be inferred from a matching folder title or a trailing release year paired with a recognized release tag such as `(Digital)`.
- For title-first filenames, use explicit volume, chapter, or page markers to separate the title from release-specific text. Preserve trailing provider names, resolution, checksums, and completion markers such as `Fin` in the original filename.
- Show the proposed group, original paths, title evidence and available covers. Let the owner link the group to a series together. Series assignment can be saved while individual chapter coverage remains unresolved. Conflicting edition evidence or multiple plausible series stays in review.
- Once a file has a unique, verified work, edition, coverage and destination, or an owner confirms the association manually, intake can move it through the managed import workflow. Opening a file depends on its format and access permissions. Files can be read while their series or chapter assignments are pending. [Import requirements](FILE-ORGANIZATION.md#import-intake-and-association)

## Chapter coverage and variants

Prefer links to individual chapters. A volume file links to every verified chapter it contains. Record page boundaries when available. Keep a series or volume association with unresolved chapter coverage when the source gives only a volume number. The owner can review and complete chapter links later.

Give each logical chapter a stable identity. Account for the work, language or edition, exact chapter and part, and volume or numbering scheme when needed. Chapter 1 in two volumes with independent numbering has two identities. A chapter-only file and a volume that includes the same chapter can reference that chapter together. Preserve the coverage of other chapters and unique pages when comparing variants.

## Paths, hashes and associations

| Record | Identity and use |
|---|---|
| File location | Keep the configured root, relative path, filesystem identity when known, and current availability. Multiple locations can reference the same content. Track hardlink aliases as physical references. |
| Content | Stream and verify a strong SHA-256 digest for an archive or PDF. For loose pages, fingerprint the ordered page bytes and page occurrences. Reuse one content record per verified digest within the server, including repeated scans and exact copies. A digest failure leaves the location available for review. |
| Content association | Store explicit links from content to the logical series, volume or chapter and the verified page coverage. Store each content–chapter pair once. A content record can cover many chapters. A chapter can have several different hashes linked as identifiable variants. |

After a rename, manual move or library relocation, match the verified content digest and attach the new location to the existing content and associations. Keep progress, manual metadata and logical chapter identities. If both paths still exist, preserve both locations. A changed digest at the same path creates or reuses its own content record and asks for review before inheriting prior links.

The read-only scanner keeps the original inventory IDs for path rows and assigns stable content IDs after hashing. A verified content record persists even if a scan removes its last indexed location. Series, volume and chapter associations can use that content ID when those workflows are implemented.

Different hashes with explicit links to the same chapter should appear together in duplicate review. Show covers, file paths, release group, edition, coverage and available page evidence so the owner can compare versions and choose what to keep. Titles and cover similarity are matching evidence. Revalidation, recovery and the managed file policy govern any move or cleanup. [Duplicate review and safety](FILE-ORGANIZATION.md#3-duplicate-manager-ux-and-resolution)

## Cover previews during scans

Series covers first use a valid `cover.png`, `cover.jpg`, `cover.jpeg`, or `cover.webp` in a directory whose indexed files resolve to one series. Try numbered folder variants such as `cover-1.jpg` next. When folder artwork is unavailable, choose the lowest owned release with a usable file cover. Order numerically by volume when supplied and otherwise by chapter. Within a volume, prefer the full volume, then the lowest chapter. Try unnumbered releases in path order after numbered releases. Retain original paths and cover provenance.

Show covers alongside original and normalized titles during association review. Equal or visually similar covers can raise the ranking of a series candidate. Conflicting edition, language or chapter evidence still needs review. Files with an unavailable cover stay visible and eligible for association and reading.

Each CBZ, image ZIP, or supported CBR/RAR archive can have an independent file cover. Prefer a readable page marked `FrontCover` in `ComicInfo.xml` using the reader's zero-based page order. Then try an explicitly named `cover` or `front-cover` image inside the archive, followed by the first readable page. The file inventory shows these covers even when its series uses different artwork. RAR artwork supports single-volume, non-solid, unencrypted archives. [ComicInfo schema](https://github.com/anansi-project/comicinfo/blob/99e1453a163c777b4b5320a68732f6f133ac7918/schema/v2.0/ComicInfo.xsd)

In Library and Import, show a thumbnail beside each filename in the Files view and inside a series. A file with no indexed artwork can use its series cover, including while an older catalog awaits a rescan. Display a placeholder when both are unavailable.

Store high-quality WebP previews in a persistent, content-addressed cache under the server data directory. Encode at up to 2048 pixels on the long edge and retain the input dimensions for smaller artwork. Use one stored copy for identical image bytes and reuse file cover results by the verified archive digest. Update cover selection on rescan. Rescan an existing library to populate file covers after upgrading.

A later update must let the owner click a cover to see a larger preview and choose preferred series artwork from multiple folders, file covers, or metadata sources. Show provenance and remember the explicit choice across rescans.

## Acceptance cases

- `Northbound/01.cbz`, `Northbound/Ch. 1.5.cbz` and `Northbound/Vol. 02.cbz` can be assigned to one series together. The volume retains unresolved chapter coverage until it is verified.
- `Northbound [Group A] [Ongoing]` and `Northbound [Group B]` can suggest the same series. A similarly titled translation or edition remains distinguishable, even with the same cover.
- `A Kiss with a Cat v01 (2022) (Digital) (1r0n)`, `Akumetsu - Tome 01 [CBR] [Scan DP] [FR] [1500x1200]` and `Undead Unluck 107 (2022) (Digital) (1r0n)` group with the other releases of their respective series. Title numbers such as `Different Title 2` stay in the title when numbering evidence is absent.
- `Undead Unluck/Vol.01 Ch.0001 - Undead and Unluck (fr) [Unexpected Team].cbz` groups under `Undead Unluck`. A numbered subtitle in a generic intake folder with no series ancestor remains an individual entry.
- A volume containing chapters 1–4 links to all four chapters. A differently packaged chapter 1 appears alongside the volume's chapter 1 for review. Chapter 1 in a different volume or edition remains separate when the numbering differs.
- Moving the same bytes between roots or renaming them preserves the content ID, associations and progress while updating available paths. Keeping both copies yields two locations and one content record. Replacing the bytes at an existing path keeps the older association available for review.
- A valid `cover.png` is used first. Without it, a supported first page is selected. Corrupt or missing images leave the file listed. Cover previews help rank suggestions without deciding a move or cleanup.
