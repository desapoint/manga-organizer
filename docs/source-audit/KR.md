# KR — KiranTheRam/mangarr

Pinned revision: [09238b58405fb94a5c753405475e30a3b4d5cb78](https://github.com/KiranTheRam/mangarr/tree/09238b58405fb94a5c753405475e30a3b4d5cb78). Reviewed 2026-11-24 (Toronto).

## Scope

244 tracked blob paths enumerated without truncation. Four targeted implementation files inspected at this revision; declaration extracts and selected logic reviewed, not a line-by-line audit of every file. [Full path index](index/KR-files.csv), [machine-extracted settings/routes/symbols](index/KR-signals.csv) from [backend/mangarr/settings_service.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/settings_service.py) (52 signals). Classifications are heuristic. Runtime tests not run.

| Path class | Count |
|---|---:|
| build/deploy | 9 |
| config/resource | 6 |
| documentation | 1 |
| other | 8 |
| source candidate | 119 |
| test candidate | 101 |

Top-level distribution: backend (186), frontend (44), .github (3), docker (2), scripts (2), .forgejo (1), .gitignore (1), LICENSE (1), README.md (1), ca_profile.xml (1), docker-compose.yml (1), templates (1).

## Observations from source
- [models.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/models.py#L51-L99): Series stores AniList and MangaUpdates IDs, provider/English/romaji/native titles, a title lock, search overrides, root and folder pin, monitor mode/from threshold, source order and blocked sources. Chapter identity needs an explicit migration design: the monitor-from field is floating-point. Do not inherit that representation for exact chapter identity.
- [settings_service.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/settings_service.py#L22-L125): default title provider, chapter naming with/without volume, optional direct image conversion (WebP 85, no width limit), source order, account/language, Torznab, torrent query templates, scheduled torrent fallback off, max pack 30 GiB/min 1 seeder, hardlink default, finished-series slow mode 7 days, 60-minute monitor, 6-hour list sync, scan-on-add, webhook and Kavita controls. VIZ and Wikipedia are metadata-only; do not count as download providers. Per-source settings include opt-in source availability and proxy routing; our implementation must respect authorized access.
- [torrent_selection.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/torrent_selection.py#L91-L281): separate metadata path extraction, title coverage, release match and candidate inspection functions. Candidate for pack coverage over raw release popularity; verify malformed metadata, unknown volume mappings and duplicate chapters.
- [api/settings.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/api/settings.py#L28-L152): retained-secret helper, settings GET/PUT, webhook/Kavita/qBittorrent connection tests, image sample and preview routes. Inspect actual URL-binding logic and redaction before copying behavior.
## Additional source-tree leads
API keys, calendar, issues, library import, series, queue; archive member validation, ZIP limits, publication, move, rename, cleanup, volume merge; chapter metadata, release schedule, metadata issues, browser setup, sign-in throttle, source registry/routing. See path index. Read tests for aliases, publication and image provenance; no claims of test execution.
## Design implications
Prefer conservative non-destructive adoption, dry-run rename, source-specific metadata/edition links and coverage scoring. Treat per-source controls and finish policy as separate settings, not one global checkbox. No reader in design; separate reader layer needed.
