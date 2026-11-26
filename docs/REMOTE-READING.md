# Remote reading and device sync

The owner requires one primary server for the main library and its reading progress. The primary can run on Windows or on a qualified Linux deployment, including Unraid/Docker. Android, Windows and Linux devices connect as clients. Optional subscriptions let a client browse and read libraries on additional servers. [Approved baseline](IMPLEMENTATION-DECISIONS.md)

## Server and client responsibilities

- The primary serves the main library from its configured storage. It owns the canonical catalog and validates reading-progress updates for that library. Reading on the server machine uses the same client and server contract as remote reading.
- The first reader uses one owner account in the shared WebUI. The owner can open the same chapter on Android, Windows and Linux and resume at the position saved by another device. The reader offers right-to-left paging and continuous vertical scrolling for webtoons. Native clients, household accounts, offline reading and third-party reader integrations are deferred.
- Each server subscription has its own endpoint, credentials and library namespace. Switching servers selects a different library. The client keeps cached item IDs and any progress for that context separate, even when servers use the same title, account name or chapter ID. The selected primary remains the authority for the main library. Progress synchronization for subscribed libraries is deferred pending a separate policy.
- Use authenticated API access for remote reading. Library access and progress updates are scoped to the account, and each connection uses credentials issued by its server. A server subscription grants access only to what that server exposes to the account.

## Reader formats

| Stage | Included formats | Acceptance |
|---|---|---|
| First reader | CBZ, image ZIP, and single-volume, non-solid, unencrypted CBR/RAR with JPEG, PNG and WebP pages | Open a chapter through the WebUI, read in page order and resume from a second client. |
| Next formats | More RAR variants and loose image folders | Verify solid and split RAR variants and folder page order. Use the same reading and progress contract as CBZ and ZIP. |
| Later formats | CB7/7z, PDF and fixed-layout EPUB | Qualify each decoder on Windows and Docker and verify that its page anchors survive ordinary client handoffs. |

Reflowable EPUB and text annotations are deferred. The format stages are implementation targets. [Current catalog support](FILE-ORGANIZATION.md#read-only-catalog)

Files in the library and import intake are eligible for reading when their format is supported. An unmatched file can open by its catalog identity while its metadata association is pending. [Import intake and association](FILE-ORGANIZATION.md#import-intake-and-association)

## Progress validation

The WebUI MVP reads CBZ and image ZIP in a right-to-left page layout. The server verifies archive bytes and bounds archive entries and page sizes. Files can open from Library or Import intake while metadata is unresolved. Resume entries use the verified content ID, so a rescan after a rename or move can find the same position.

The MVP saves page position automatically as pages load. Finishing a file is a separate action. For series counters, a finished file counts its verified or explicitly numbered chapters once. Reopening an archive whose bytes changed creates a separate reading identity once scanned.

The server validates the authenticated account, chapter or edition, content version and page or position before accepting an update. Store progress against a stable logical chapter and a page anchor so a rename, move or verified equivalent repack preserves the resume point. If the content changes and the position cannot be mapped safely, retain the previous anchor for review.

Use server-issued revisions and idempotent update IDs as the initial synchronization design. A retry records an event once, and a stale update after reconnect cannot silently replace a newer accepted position. A deliberate backward move on the current revision remains valid. Keep resume position and completion as separate state. The exact conflict UI, download-for-offline support and progress from device-only content before it is linked to a server edition remain separate design choices. [Reader data contracts](source-audit/READER-DATA-CONTRACTS.md)

## Optional upload from a device

A client may keep its own files and explicitly send selected items to the primary. The server stages the upload, validates the archive and destination, and checks for exact duplicates and edition or coverage conflicts. Publication uses an authorized managed root and the same confirmation and revalidation rules as other imports. A completed upload leaves the original on the device. Failed or retried uploads preserve existing library files and result in at most one catalog entry and managed copy for the approved import. Local progress can be associated with a server entry after the edition, chapter and page mapping are verified. [File organization and duplicate rules](FILE-ORGANIZATION.md)

## Acceptance cases

| Scenario | Pass condition |
|---|---|
| Android, Windows and Linux handoff | Read a synthetic chapter on each platform and resume on the next with the same account. Repeat with a Windows primary and a Linux/Unraid primary. The client can stream from the server without copying the full library locally. |
| Conflicting progress and reconnect | Submit two updates from the same base revision and replay one request. Stale or repeated requests never silently overwrite newer progress. An intentional backward move from the current revision succeeds. |
| Library move or replacement | Rename or relocate the server file and verify resume survives. When a replacement has different page content, preserve the prior anchor and require a verified mapping. |
| Optional server subscriptions | Connect to two servers whose libraries share an item ID or title. Switch libraries and verify isolation of credentials, item identity and progress context. The main library still uses the primary for sync. |
| Optional device upload | Send a synthetic archive twice with an interrupted transfer in between. The server admits one copy, preserves the local original and an existing conflicting destination, and reports the importer outcome to the client. Test unauthorized and invalid uploads using disposable fixtures. |

These are product acceptance cases. Pass 22 verified a Suwayomi backup export and the Mihon wire codec. Live device handoff, subscription isolation and client uploads still need their own evidence. [Audit completion ledger](source-audit/COMPLETION-LEDGER.md)
