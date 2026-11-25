# MK — OrbitMPGH/Maki

Pinned revision: [0de45e926f368e23ace4dba04e2211d1de83aadb](https://github.com/OrbitMPGH/Maki/tree/0de45e926f368e23ace4dba04e2211d1de83aadb). Reviewed 2026-11-24 (Toronto).

## Scope

3,755 tracked blob paths enumerated without truncation. Four targeted implementation files inspected at this revision; declaration extracts and selected logic reviewed, not a line-by-line audit of every file. [Full path index](index/MK-files.csv), [machine-extracted settings/routes/symbols](index/MK-signals.csv) from [src/Maki.Core/Configuration/IAppSettings.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Core/Configuration/IAppSettings.cs) (231 signals). Classifications are heuristic. Runtime tests not run.

| Path class | Count |
|---|---:|
| build/deploy | 127 |
| config/resource | 23 |
| documentation | 416 |
| other | 131 |
| source candidate | 2197 |
| test candidate | 861 |

Top-level distribution: src (1304), frontend (947), tests (860), site (227), distribution (109), design-system (78), changes (49), scripts (47), docs (43), locales (22), .agents (21), .claude (19).

## Observations from source
- [IAppSettings.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Core/Configuration/IAppSettings.cs#L39-L157): Prowlarr/Jackett backend, selected indexers/categories; client kind, label, path mapping, removal after import/seeding, ratio/time, stall timeout, delete-data flag. Kavita path mapping and explicit mapped user; comments identify one API key as one reader. Suwayomi base/subpath and none/basic/UI-login modes; installed sources snapshot so registry can start while engine offline. Independent custom-source definitions/repositories. Monitor default modes All/MainOnly/Smart, special exclusions; bulk hold threshold (5 by default, per comments) limits mass acquisition when source renumbers. Optional read-file cleanup and last-read retention. Settings constants and comments are evidence of configured intent, not proof every consumer enforces defaults.
- [SuwayomiController.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Controllers/SuwayomiController.cs#L28-L299): admin connection get/save/test, refresh sources, configure source, thumbnail route. This is not the same as installing APKs; compare UC extension lifecycle.
- [CustomSourceRepositoriesController.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Controllers/CustomSourceRepositoriesController.cs#L16-L70): admin list/add/remove repository, auto-update, refresh, install, check/apply/revert update. A custom source format is separate from Mihon extension format; require separate trust policy.
- [UpgradeProfilesController.cs](https://github.com/OrbitMPGH/Maki/blob/0de45e926f368e23ace4dba04e2211d1de83aadb/src/Maki.Api/Controllers/UpgradeProfilesController.cs#L20-L73): CRUD upgrade profile API, admin mutations. Corrects initial assumption that upgrade controls are only partial; exact scoring rules need review.
## Additional source-tree leads
Controllers for auto-tag rules, custom rails, discover filters, drop-folder import, credit groups, chapter folds, notes, settings page share, shortlist, scanlation groups, series requests, root folder rules, quality formats and upgrades; jobs for related-work watch, followed creators, automated read status, drop folder, torrent removal, upgrade scans, scheduled backups, embedding/co-read/taste vectors, cache idle unload. These are distinct candidate surfaces, not yet behavior-verified. Explore service and UI pairs in index.
## Design implications
Strongest integrated baseline for configurable UI and worker design. Explicitly separate custom adapter repositories from APK/JAR stores; protect seed data by default and expose settings with clear effects. Do not assume every paid/restricted model or catalog can be bundled.
