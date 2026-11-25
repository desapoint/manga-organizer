# KV — Kareadita/Kavita

Pinned revision: [7fcf1085a5355a8c592d17adcfbb28b69276ae40](https://github.com/Kareadita/Kavita/tree/7fcf1085a5355a8c592d17adcfbb28b69276ae40). Reviewed 2026-11-24 (Toronto).

## Scope

4,123 tracked blob paths enumerated without truncation. Four targeted implementation files inspected at this revision; declaration extracts and selected logic reviewed, not a line-by-line audit of every file. [Full path index](index/KV-files.csv), [machine-extracted settings/routes/symbols](index/KV-signals.csv) from [Kavita.Server/Controllers/ReaderController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/ReaderController.cs) (39 signals). Classifications are heuristic. Runtime tests not run.

| Path class | Count |
|---|---:|
| build/deploy | 20 |
| config/resource | 103 |
| documentation | 13 |
| other | 616 |
| source candidate | 3028 |
| test candidate | 343 |

Top-level distribution: UI (1880), Kavita.Models (712), Kavita.Database (526), Kavita.Services.Tests (293), Kavita.Server (231), Kavita.Services (165), Kavita.API (126), Kavita.Common (42), scripts (38), .github (15), Kavita.Common.Tests (12), Kavita.Database.Tests (12).

## Observations from source
- [ReaderController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/ReaderController.cs#L59-L881): PDF/image/thumbnail/dimensions/chapter info, individual/bulk chapter/volume/series read/unread, progress get/write, continue point and presence, per-chapter/volume/series bookmarks and bulk removal. Each needs independent capability and permission checks; API presence is not UI parity.
- [KoboController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/KoboController.cs#L37-L383): API-key scoped route with download policy, initialization, library sync, metadata, state GET/PUT, EPUB delivery, thumbnails and library add/delete. Product/review/recommendation endpoints also exist; verify implementation vs compatibility stub and premium boundary.
- [PluginController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/PluginController.cs#L29-L159): plugin authentication validates key and login/admin role, returns user tokens/version; version and auth-key expiry; library-type-specific title/year/chapter/volume parsing, regex timeout handling and 100-item bulk limit. API explicitly not fully built out. Not a source extension runtime.
- [ServerSetting.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Models/Entities/ServerSetting.cs): persisted server setting entity with concurrency token; defaults belong elsewhere.
## Additional source-tree leads
Controllers for CBL, annotations, KoboAnnotation, Koreader, Tachiyomi, Panels, OPDS, stream, reading profiles, filter, people, devices, fonts, theme, ColorScape, locale, stats, user roles, OIDC, upload, KavitaPlus audit and scrobbling. Scanner services and database filtering/repositories, media errors, file cache, task scheduler. Inspect access policies, EPUB rendering, device sync, scan algorithms and premium entitlements before adoption.
## Design implications
Reference for mature reader/library API, filename parsing and device sync. Distinguish downloading an owned file to a reader from acquisition from a remote source. Preserve annotations/bookmarks and explicit read state; Kobo optional unless selected.
