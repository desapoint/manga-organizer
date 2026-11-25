# Feature selection matrix

[D] documented, [P] partial/different workflow, [$] paid service, [N] explicitly absent/not applicable, [?] not established by inspected evidence (not absence). Columns follow [reference IDs](../REFERENCES.md). Each row is a candidate cluster; lead is a proposed design benchmark, not runtime ranking. Required = user-requested outcome, not implementation approved. Others pending. Edit [CSV](FEATURE-SELECTION.csv) for decisions.

| ID | Feature | KR | MK | UC | KK | KV | SW | MH | Lead |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F01 | Multi-root scan and adoption | D | D | D | D | P | ? | ? | UC/KR |
| F02 | Metadata identity aliases overrides | D | D | D | D | D | ? | ? | MK/KK |
| F03 | Chapter volume decimal special omnibus pack model | D | P | P | D | P | ? | ? | KK |
| F04 | Language editions and progress | ? | D | D | P | ? | ? | ? | UC |
| F05 | Grid list search filters bulk editor | D | D | D | D | D | P | P | UC/KV |
| F06 | Collections saved filters shared lists CBL | ? | P | D | P | D | P | P | KV/UC |
| F07 | Local catalog cross-provider IDs | P | D | P | P | ? | ? | ? | MK |
| F08 | Related works recommendations discovery | D | D | D | ? | $ | P | ? | MK |
| F09 | Tracker list import and review exclusions | D | D | D | D | P | ? | ? | UC/KK |
| F10 | Mihon backup categories and progress import | ? | D | D | ? | ? | D | P | MK/UC/SW |
| F11 | Direct source adapters priority matching | D | D | D | P | N | P | ? | MK/KR |
| F12 | Mihon-compatible extension downloading via Suwayomi | ? | D | D | D | N | D | P | SW/UC |
| F13 | Torrent indexers Prowlarr Jackett Torznab | D | D | ? | D | N | ? | ? | KK |
| F14 | Nyaa exact pack file coverage | D | P | ? | P | N | ? | ? | KR |
| F15 | Newznab Usenet SABnzbd | ? | ? | ? | D | N | ? | ? | KK |
| F16 | qBittorrent Transmission Deluge | P | D | ? | P | N | ? | ? | MK |
| F17 | Remote path mapping client health | P | P | P | D | N | ? | ? | KK |
| F18 | Monitoring wanted manual search exclusions | D | D | D | D | N | D | P | KK |
| F19 | Adaptive checks estimated release calendar | P | D | D | P | N | P | ? | MK/UC |
| F20 | Quality profiles custom formats delay language | P | P | P | D | N | ? | ? | KK |
| F21 | Translation group preference wait upgrade | ? | P | D | P | N | ? | ? | UC |
| F22 | Source fallback replacement rate limits | D | D | D | P | N | ? | ? | UC |
| F23 | Live queue history retry blocklist | P | D | P | D | N | P | ? | KK/MK |
| F24 | Durable backfill partial download repair | ? | P | D | P | N | ? | ? | UC |
| F25 | Auto manual import conflict review | D | D | D | D | P | ? | ? | KK |
| F26 | Copy move hardlink seeding | D | D | ? | D | N | ? | ? | KK/KR |
| F27 | Rename preview no overwrite | D | D | P | D | ? | ? | ? | KR/KK |
| F28 | CBZ ComicInfo cover provenance | D | D | P | D | P | ? | ? | KR/MK |
| F29 | Archive PDF loose image reading | N | P | D | N | D | P | P | KV |
| F30 | Split RAR volume coverage assembly | P | P | ? | D | P | ? | ? | KK |
| F31 | Image conversion preview batch | P | D | ? | ? | ? | ? | ? | MK/KR |
| F32 | Integrity duplicates gaps repair | P | D | D | D | ? | ? | ? | UC |
| F33 | NAS mount recovery storage guards | P | P | D | D | ? | ? | ? | KK |
| F34 | Quarantine restore retention deletion | P | D | D | P | ? | ? | ? | UC/KK |
| F35 | Paged RTL spread continuous webtoon | N | D | D | N | D | P | D | UC |
| F36 | Split join crop rotate filters controller | N | P | D | N | ? | ? | P | UC |
| F37 | Reader profiles thumbnails auto-scroll | N | D | P | N | ? | ? | P | MK |
| F38 | Local enhancement upscaling | N | D | ? | N | ? | ? | ? | MK |
| F39 | OCR translation credit collapse | N | ? | D | N | ? | ? | ? | UC |
| F40 | Offline PWA prefetch progress | N | D | D | N | ? | P | P | UC/MK |
| F41 | Progress resume tracker scrobbling | N | D | D | N | $ | D | D | MK/MH |
| F42 | Incognito moments bookmarks notes | N | P | D | N | P | ? | ? | UC |
| F43 | Stats goals streaks annual recap | N | D | D | N | ? | ? | ? | UC/MK |
| F44 | Dashboard themes localization | P | D | D | P | D | P | P | KV/UC |
| F45 | Text EPUB annotations | N | ? | N | N | D | ? | ? | KV |
| F46 | Kobo annotation sync | N | ? | N | N | D | ? | ? | KV |
| F47 | OPDS-PSE KOReader Komga API | N | D | D | N | ? | P | ? | MK/UC/SW |
| F48 | Kavita scan progress integration | D | D | ? | ? | N | ? | ? | KR/MK |
| F49 | Multiple users per-root grants progress | ? | D | D | N | D | ? | ? | UC/KV |
| F50 | Age ratings private copies adult isolation | ? | P | D | N | P | ? | ? | UC |
| F51 | Household comments requests leaderboard | N | P | D | N | ? | ? | ? | UC |
| F52 | Login 2FA OIDC scoped tokens audit | P | D | D | P | P | ? | ? | UC |
| F53 | Notifications webhook push | P | D | D | D | ? | ? | ? | MK/KK |
| F54 | Backup restore migrations rollback | P | D | D | D | ? | D | P | KK/SW |
| F55 | REST and Sonarr v1/v3 compatibility | P | P | P | D | ? | P | ? | KK |
| F56 | Health logs scheduled tasks | P | D | D | D | ? | P | ? | KK |
| F57 | Windows standalone and remote modes | ? | P | D | ? | P | P | N | UC/MK/SW |
| F58 | Docker Unraid WebUI | D | D | D | D | P | D | N | KK/MK/SW |
| F59 | Opt-in telemetry privacy | ? | P | D | ? | ? | ? | ? | UC |
| F60 | Title artwork banners anime tie-in | ? | P | P | ? | ? | ? | ? | UC/MK |
| F61 | Extension repository management install update remove | ? | P | D | ? | ? | D | ? | SW/UC |
| F62 | Extension search browse latest chapters | ? | P | D | ? | ? | D | ? | SW |
| F63 | Extension settings trust and source identity | ? | P | D | ? | ? | P | ? | SW/UC/MH |
| F64 | Extension engine version and compatibility diagnostics | ? | P | P | ? | ? | P | ? | SW |
| F65 | Mihon connector and progress interoperability | ? | D | D | ? | ? | D | P | SW/UC |
| F66 | Additional trackers Shikimori Bangumi Hikka | ? | ? | ? | ? | ? | ? | D | MH |
| F67 | Mihon mobile reading categories and migration | ? | P | P | ? | ? | P | D | MH |
| F68 | Single unified download queue and authoritative library | P | D | P | D | P | P | ? | MK/UC |

| F69 | Physical file organization and bulk rename/move | D | D | P | D | P | ? | ? | KR/KK/MK |
| F70 | Duplicate detection, review and safe resolution | P | D | D | P | ? | ? | ? | MK/UC/KK |

F69 and F70 are required user outcomes; see [file organization specification](FILE-ORGANIZATION.md). The new rows summarize existing documented/inspected workflows, not complete parity. F01/F25/F27/F28/F32/F33/F34 are related prerequisites or overlapping clusters, whose additional scope remains pending. Similarity detection and automatic cleanup policies are proposed, not approved.

## Selection rules

Choose Include / Optional / Defer / Exclude for Pending features. Required rows F12, F57, F58, F69 and F70 record explicit requirements; exact implementation remains open. Split clusters when individual choices differ. Preserve evidence for exclusions. Priorities are proposals, not approvals. Add prerequisites, acceptance criteria and references to every accepted issue/PR/test. One scheduler, source-independent chapter identity, preserve original files, distinguish managed from read-only, one authoritative remote server, and explicit progress rather than prefetch-counting are proposed defaults.
