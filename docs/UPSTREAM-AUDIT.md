# Upstream feature audit — baseline v0.1

Evidence: [registry](../REFERENCES.md). Documentation-level review of seven READMEs plus Maki's detailed features and Kha-kis' parity inventory. No source-level verification, runtime tests or performance benchmarks. “Best” means strongest documented fit for a particular workflow, not measured superiority.

## KR — KiranTheRam/mangarr

**Role:** automation-only; Python/FastAPI, React, SQLite; Docker, no built-in reader.

- MangaUpdates-first metadata, AniList fallback, aliases, chapter/volume counts and release feed; poster grid, monitored chapters, wanted list, existing-library adoption, manual unmatched file mapping, folder reassignment, scan and rename preview without overwrite; preserve PDF/CBR/loose images, volume and chapter-range coverage.
- Import AniList/MAL/MangaDex/MangaUpdates lists, related works, recommendations and estimated release calendar.
- Direct adapters documented for MangaPlus, WEBTOON, MangaDex, TCB, MangaFire, FanFox, WeebCentral, Atsumaru, Asura, Toonily. Some opt-in; availability varies. Source priority, aliases, per-chapter search, scheduled checks and rate limiting.
- Nyaa and Torznab (Prowlarr/Jackett), qBittorrent, query templates/overrides, pack scoring by actual missing chapter coverage, seeders and size; scheduled torrent fallback opt-in, unknown coverage not assumed complete.
- CBZ + ComicInfo; atomic publishing, hardlink/copy fallback; opt-in WebP/JPEG preview and resizing with provenance, preserve unsupported/animated images, no existing file recompression.
- Kavita series/library scan batching, import webhook, named revocable API keys, login/reset, host validation, destination-bound secrets, Docker health, UID/GID/umask, SQLite backups.
- Roadmap: Western comics and more notifications; not implemented claims.

**Best reference:** coverage-aware pack selection, simple adoption and conservative image provenance. Less configurable than KK and no reader.

## MK — OrbitMPGH/Maki

**Role:** all-in-one .NET/Node app; Windows installer/portable and Docker/Unraid, pre-1.0.

- Local MangaBaka catalog, identity links to MAL/AniList/Kitsu/MangaUpdates, local embeddings, genre rails, blind spots, reader similarity, taste trends, filterable discovery and detail/reviews. Required initial catalog download and data license/storage costs.
- Adaptive checks, release prediction confidence, automatic source mapping, sampled quality comparisons, priority and per-chapter wanted. README lists 35 built-in multilingual sources including publisher APIs and third-party sites, plus Suwayomi.
- Prowlarr/Jackett and qBittorrent/Transmission/Deluge, reject related-work mismatch, live shared queue/retry/review. CBZ conversion of archives/loose images, PDFs preserved, hardlinks where possible, naming, cover.jpg, ComicInfo, atomic write.
- Multilingual files and chapter rows, title preference, grid/list, tags/saved filters/bulk wanted, archive health and reviewed replacement/deletion.
- Paged, spread, original size, scroll, crop/split/rotate, auto-scroll, thumbnails, reading time estimates, reading profiles, auto tall-page mode, customizable home, incognito, stats/achievements, anime-to-manga start point.
- Tracker import and scrobble (AniList/MAL/Kitsu/MangaBaka/MangaUpdates), Mihon backup categories/read-state, Kavita progress merge, OPDS-PSE, KOReader, Komga-compatible API, format transcode, offline PWA and unread prefetch.
- Opt-in WebP/AVIF/JPEG XL per-page rules, preview/batch conversion, savings threshold, skip seeding hardlinks, retained originals and disk guard. Non-destructive WebGL/ONNX enhancement, comparison, preload/cache/CPU limits, prepared chapters and fallback.
- Multiple users, root grants, 2FA/OIDC, API keys, trusted proxies, lockout, backups before migration, push and Discord/Telegram/Slack/Notifiarr/ntfy/Gotify/Pushover/Apprise/webhooks, themes, localization, access logs.

**Best reference:** broad integrated baseline, discovery, encoding/enhancement and Windows packaging. API/schema may change; KK has deeper documented policy engine.

## UC — AngeloSha/uchiyomi

**Role:** server/downloader, webtoon reader; TypeScript, documented Postgres stack; Windows/macOS dual-mode desktop beta.

- Existing CBZ/CBR/PDF/image EPUB/image folders; flexible layout, multi-root libraries, metadata overrides, merge/delete/restore/forget, bulk operations, language editions with independent progress, title artwork and banners.
- Search/filter/sort/author browsing, ratings, related works, smart/shared lists, customizable home, recommendations/trending, progressive multi-source discovery, read before adding.
- MangaDex, generic site adapters and user-selected Mihon/Tachiyomi extensions through optional Suwayomi; settings, search, fallback, replacement review, translation group prefer/block/wait/upgrade and version choice.
- Missing chapter rows, resumable backfill, partial download repair, dedup across numbering variants, webtoon episode parts, rate-limit backoff, source health stages, adaptive monitoring and release radar. Upstream documents solver redundancy; do not implement access-control bypass.
- Health finds gaps, short/corrupt/duplicate files, missing mounts, disk full, tracker/source failures; previewed fixes, logs, opt-in scheduled repair and file verification; notice exclusions; optional deletion of managed read chapters.
- Cross-chapter vertical scroll, paged/RTL/spread, split/join/crop, grayscale/invert/e-ink, keyboard/tap/controller, device profiles, credit-page collapse, optional local OCR/translation, offline favorites, moments/notes, incognito, detailed stats/Wrapped.
- Household favorites/progress, private copies, shared lists, age caps/hidden adult libraries, comments/reactions, leaderboard, 2FA/OIDC, audit/session/lockout, scoped tokens, nightly backup.
- Reviewed Mihon/list imports, AniList/MAL/Kitsu/MangaUpdates sync, OPDS-PSE, KOReader original-file matching, Mihon extension, Komga API, Homepage, webhook/ntfy/Discord/Home Assistant/push, nine languages including Arabic RTL, opt-in telemetry.

**Best reference:** extension UX, dual-mode desktop, webtoon reading, source health, household privacy and translation-group selection. No torrent/Usenet policy engine documented; text EPUB explicitly absent. Native mobile/per-source genre browsing are planned.

## KK — Kha-kis/manga-arr

**Role:** configurable Servarr-style automation; Python, templates, SQLite, single admin.

- Manga/light novel model: chapters/decimals/volumes/editions/omnibus/specials/one-shots/packs; AniList/MangaDex/MangaUpdates/Kitsu discovery, reconciliation/retry/coverage health.
- Prowlarr/Torznab/Newznab RSS/backlog and Suwayomi direct handoff; qBittorrent/SABnzbd, remote path mappings, connection tests, categories, circuit resets and retries/blocklist.
- Quality profiles/definitions, scored custom formats JSON import/export and preview, release/language/delay profiles, upgrade cutoff, wanted/cutoff-unmet, manual pack search, import-list exclusions, tags and series editor.
- Auto/manual import, retry/skip/dismiss, CBZ/CBR/split RAR, copy/move/hardlink, duplicate-quality checks, ComicInfo, free-space guard, adoption, rename preview, history/calendar/stats/recycle bin.
- Komga scan, Discord/ntfy/Gotify/Apprise/Pushover/Pushbullet/Slack/email/webhooks; tasks/logs/backups/updates/health, partial Sonarr v1/v3 API compatibility and route tests.
- Encrypted credentials, single admin, nonroot, consistent backups with matching key, NAS recovery fences and qualification. README distinguishes stable 1.3.2 from unqualified 1.3.3 candidates; RC10 held, full remount continuity deferred, volume quarantine experimental/off by default, progress not transferred.

**Best reference:** policy/scoring, Usenet, exact compatibility inventory and data recovery gates. Not a reader; greater setup complexity.

## KV — Kareadita/Kavita

**Role:** cross-platform reading server (C#); no acquisition manager documented.

- CBZ/CBR/ZIP/RAR/RAR5/7zip/raw images; EPUB/PDF books; responsive readers, webtoon/continuous modes, virtual EPUB pages, annotations/highlights.
- Metadata search/filter, smart filters, collections, CBL reading-list import, Want to Read, configurable dashboard/sidebar, themes and localization.
- Role-based users, age controls, OIDC, Kobo sync (EPUB/archives/images, annotations, Kepubify).
- Paid Kavita+ includes internet-facing metadata, reviews/ratings, recommendations, scrobbling, smart collections; investigate individual entitlements before equivalent design.

**Best reference:** format breadth, ebook reader, Kobo, library presentation. Wiki/code review needed for detailed scanning, OPDS/API and free/paid distinctions.

## SW — Suwayomi/Suwayomi-Server

**Role:** independent JVM server compatible with Mihon/Tachiyomi extensions, not a Mihon fork. Windows/macOS/Linux bundles, Docker, browser/PWA clients.

- Installs and executes compatible third-party extensions, searches/browses installed sources, categories/library, automated update and download, latest chapters, offline content, Mihon-compatible backups and automated backup creation.
- Trackers including MAL/AniList/MangaUpdates, OPDS/OPDS-PSE, automated WebUI updates, integrated WebUI/VUI; optional WebView dependencies for some extensions.
- Java 21+ for direct JAR; bundled OS distributions; official Docker and remote access. Server feature availability depends on client implementation.
- Mihon connector extension exposes server library/categories; tracker updates progress from Mihon, reverse sync requires opening track information per README. Neko merge integration has separate two-way behavior.

**Best reference:** actual extension runtime on desktop/server. Reuse via a versioned adapter instead of porting Android APK execution. Extensions are third-party and compatibility is not universal; inspect API, trust, versioning and file output before implementation.

## MH — mihonapp/mihon

**Role:** native Android reader (Android 8+), not a Windows/server engine.

- Local reading, configurable viewers/directions, categories, light/dark themes, scheduled library updates, local/cloud-target backups.
- Trackers: MangaBaka, MAL, AniList, Kitsu, MangaUpdates, Shikimori, Bangumi, Hikka. README does not enumerate the full extension management/reader feature surface; verify app source and website.
- Extension compatibility is central to SW's documented purpose; migration formats and source IDs need code-level verification. Existing Mihon remains a mobile client target; do not assume bidirectional sync for every field.

**Best reference:** mobile reader behavior, library categories, tracker breadth, backup interoperability and extension ecosystem semantics. Use SW for execution on Windows and Unraid; avoid requiring Android emulation.

## Similarities and differences

KR/KK/MK/UC/SW automate acquisition; KR ranks actual pack coverage, KK ranks policy profiles, MK combines torrents and direct sources, UC prioritizes source health/translation fallback, SW executes extensions. MK/UC/KV/MH/SW provide reading workflows, though SW's frontend determines actual support. MK/UC offer household and desktop examples; KV adds text EPUB and Kobo; MH is the Android interoperability benchmark. SW and MH share extension compatibility and backup lineage, not identical app architecture. A single canonical library, scheduler, queue and user-progress model with adapters is preferable to seven databases or competing schedulers.

## Audit completion backlog

- [ ] Re-read at pinned SHAs, enumerate source trees, API routes, UI screens, settings, migrations, tests, releases and changelogs.
- [ ] Split provider/client/API capabilities into atomic evidence records; distinguish stable/beta/premium/planned.
- [ ] Verify actual extension compatibility, install/update/trust flow, API contract and backup mapping on synthetic/authorized fixtures.
- [ ] Test identical library, import, numbering, permissions, reading and resource scenarios across projects and target platforms.
- [ ] Review licenses, data/model terms and Kavita+ boundaries.
- [ ] Owner selects feature IDs and approves architecture; only accepted features become implementation requirements.
