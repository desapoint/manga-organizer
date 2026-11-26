# Catalog identity and association

These are owner-selected requirements for the organizer and reader. Apply the workflow to both the library and import intake. [File organization](FILE-ORGANIZATION.md) · [Reader formats](REMOTE-READING.md#reader-formats)

## Group files into series

Library and import inventories show path-based series candidates as expandable groups. Grouping happens before pagination. A group can be suggested even when its chapter or volume coverage is unresolved. The inventory retains paths and content IDs so the owner can inspect the original entries.

- Use the containing folder as the series candidate when filenames contain only chapter numbers, volume numbers, ranges or numbering. Parse decimal chapters and parts without losing precision. A folder name like `Volume 02` inherits a series candidate from its parent. A generic intake folder with files from several series leaves ambiguous entries for review.
- Look for the same or a closely matching title across folders. For title comparison, normalize case, spacing and punctuation and parse recognized release group tags and status words such as `ongoing`. Retain the original names and parsed provenance. Preserve meaningful numbers, subtitles, edition and language evidence.
- Recompute suggested groups on the next scan. Recognize explicit numbering such as `v01`, `Chapter 107` and `Tome 01` before release annotations. A bare chapter number can be inferred from a matching folder title or a trailing release year paired with a recognized release tag such as `(Digital)`.
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

Look for `cover.png` beside the files and use it as the folder's cover candidate. When it is absent or invalid, use the first readable page of a supported manga file. Keep the image's provenance so the owner can see whether it came from the folder or a particular release. Create bounded previews without modifying the source, follow the existing archive, filesystem and permissions rules, and invalidate the preview when its source changes.

Show covers alongside original and normalized titles during association review. Equal or visually similar covers can raise the ranking of a series candidate. Conflicting edition, language or chapter evidence still needs review. Files with an unavailable cover stay visible and eligible for association and reading.

## Acceptance cases

- `Northbound/01.cbz`, `Northbound/Ch. 1.5.cbz` and `Northbound/Vol. 02.cbz` can be assigned to one series together. The volume retains unresolved chapter coverage until it is verified.
- `Northbound [Group A] [Ongoing]` and `Northbound [Group B]` can suggest the same series. A similarly titled translation or edition remains distinguishable, even with the same cover.
- `A Kiss with a Cat v01 (2022) (Digital) (1r0n)`, `Akumetsu - Tome 01 [CBR] [Scan DP] [FR] [1500x1200]` and `Undead Unluck 107 (2022) (Digital) (1r0n)` group with the other releases of their respective series. Title numbers such as `Different Title 2` stay in the title when numbering evidence is absent.
- A volume containing chapters 1–4 links to all four chapters. A differently packaged chapter 1 appears alongside the volume's chapter 1 for review. Chapter 1 in a different volume or edition remains separate when the numbering differs.
- Moving the same bytes between roots or renaming them preserves the content ID, associations and progress while updating available paths. Keeping both copies yields two locations and one content record. Replacing the bytes at an existing path keeps the older association available for review.
- A valid `cover.png` is used first. Without it, a supported first page is selected. Corrupt or missing images leave the file listed. Cover previews help rank suggestions without deciding a move or cleanup.
