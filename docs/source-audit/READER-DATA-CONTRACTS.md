# Reader, user state and proposed data contracts

The owner requires server-hosted reading and cross-device progress for the main library. The canonical data model below is a proposal based on pass 6 and the file organization findings. For accepted scope and verification, use the remote reading requirements. [Pass 6](PASS6.md) · [Remote reading requirements](../REMOTE-READING.md)

## Reader feature families

| Family | Independent candidates | Main references |
|---|---|---|
| Format | CBZ/CBR and supported archive/loose image/PDF; image EPUB vs text EPUB | KV/UC/MK/MH |
| Layout | Single/double, RTL/LTR, vertical/webtoon, spread alignment, landscape split/rotate, fit/zoom | MK/UC/KV/MH |
| Appearance | Theme, crop, color filters, padding, background, font/spacing/margins for text, PDF spread | MK/UC/KV/MH |
| Interaction | Tap zones, keyboard, controller where verified, navigation filters, page navigator, undo, e-ink flash | MK/UC/MH |
| Profiles | Defaults, format, source, library, title, device, explicit session override; proposed precedence below | MK/UC/KV/MH |
| Progress | Resume page/offset, explicit complete/unread, inferred completion, reread count, reading duration, per-user/device | MK/UC/KV/SW/MH |
| Notes | Chapter bookmark, page bookmark, saved image, annotation XPath/context, spoiler, export | KV/MK/MH |
| Privacy | Incognito, private library access, per-user root grants, offline isolation, tracker consent | UC/MK/KV/SW |
| Offline | Cache manifest/version, smart unread favorites, quota, owner checks, durable replay | UC/MH; MK earlier documentation |

## Proposed canonical entity separation

| Entity | Identity and relations |
|---|---|
| Work | Stable internal ID, metadata aliases and provenance; external provider+ID unique within provider namespace |
| Edition | Work, language, publication/translation edition, explicit relationship to source IDs; no title-only merge |
| Logical chapter/volume | Edition + exact decimal/string chapter identity, part/range/numbering scheme, source mapping; stable internal ID |
| Release | Logical coverage and source/group/version; may cover multiple logical chapters |
| Physical object | Root and storage identity, content hash, file length, immutable manifest version, current locations/hardlink aliases; unknown physical identity is not equality |
| Coverage | Many-to-many release/chapter/page ranges, evidence and confidence; no fabricated coverage |
| Page anchor | Original ordered page content hash, index and occurrence, logical chapter, optional normalized image region or text location; repeated identical page hashes are disambiguated by occurrence |
| Presentation transform | Split/join/crop/rotation/scale and derived page mapping; no change to original anchors unless explicit validated conversion |
| User reading state | Server namespace, account, edition and logical chapter, with preferred release/version, position and explicit completion |
| Bookmark/annotation | Server namespace, user and logical content anchor, original release/version, optional saved snapshot; orphaned/unmappable anchors preserved for review |
| Reading event/outbox | Unique event ID scoped to server and user, device, content version and server revision, event time and receipt time, consent and sync source |
| Operation journal | Approved plan/version, authority, affected references, before/after identity, state, recovery and quarantine receipt |

No automatic merge based on same title or chapter number. No foreign-key cascade that silently deletes user reading state merely because a physical file is gone. Deliberate account/library deletion must have a separate reviewed policy. User and file visibility must be checked on every API, cache, export and external sync path.

## Conflict rules (proposed)

- Resume follows the latest server-accepted revision. Stable event IDs allow safe retries, and a current revision permits deliberate backward reading. Stale updates need a separate conflict policy, including explicit completion changes and any future offline replay.
- Completion is separate from resume position. Explicit mark read/unread, inferred OPDS completion and genuinely read-through events have different provenance. Avoid counting prefetch and manual marks as reading sessions. Preserve reread totals and user-specific history.
- Incognito policy should be explicit: proposed resume-only mode suppresses history/stats/trackers; a stricter no-persist mode is a separate optional choice. Do not silently infer user consent to either. Never publish private activity to an unsupported external tracker.
- When file bytes change, page anchors must be mapped by reliable ordered content evidence. When only path changes, keep logical IDs/anchors. For repacks, verify ordered page content; for changed/split/combined pages, preserve originals and show unresolved mapping. Do not silently reset positions or move annotations to a different translation.
- Profile precedence proposed for review: temporary session override > explicit title override > explicit chosen profile > matching source/library/device rule > media-format default > global default. Resolve ambiguity deterministically and expose effective source; do not overwrite global defaults with a per-title adjustment.
- Offline caches must be account-scoped, versioned and revocable; revalidate permission on reconnect and purge inaccessible content under a documented policy. Never claim remote revocation of bytes on an offline device is instantaneous.

## Migration and verification requirements

| Fixture | Required result |
|---|---|
| Legacy single-user DB | Explicit mapping of old rows to confirmed owner; no default arbitrary account |
| Schema version upgrade | Preflight compatibility, consistent backup, transaction or resumable journal, idempotent rerun, reject unsupported downgrade |
| Rename, move, hardlink | Stable chapter/release IDs, progress and bookmarks, no duplicate activity |
| Exact repack | Ordered page anchors preserved, no unnecessary progress reset |
| Reencoded/changed/split pages | Review mapping, preserve old anchors, no silent deletion |
| Overlapping chapter/volume pack | Preserve unique coverage and per-user anchors |
| Two devices, late offline event | Newer resume not overwritten by stale replay; legitimate backward movement allowed |
| Explicit unread vs late completion | Revision/intent conflict policy tested; no unreviewed regression |
| Private library, merged series, account switch | No unauthorized listing, thumbnails, cached pages, annotations or tracker activity |
| Restore/restart/crash | Check row counts, FKs, IDs, progress, history, provenance, operations, backup integrity and rollback/recovery |

The approved stack uses SQLite in local appdata. External library roots can be network mounts after qualification. The exact schema, event transport and client storage policy remain design choices. These contract fixtures await product verification.
