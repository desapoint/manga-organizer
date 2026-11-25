# KR — second-pass settings and operational routes

Pinned [revision](https://github.com/KiranTheRam/mangarr/tree/09238b58405fb94a5c753405475e30a3b4d5cb78). Source contents fetched for the files below; patterns enumerate declarations and candidate fields only, not verified runtime semantics. Scope: 7/7 files. See [signals](index/KR-pass2.csv).

| File | Lines | Signals |
|---|---:|---:|
| [backend/mangarr/api/series.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/api/series.py) | 811 | 68 |
| [backend/mangarr/api/library.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/api/library.py) | 955 | 80 |
| [backend/mangarr/api/queue.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/api/queue.py) | 284 | 16 |
| [backend/mangarr/api/import_lists.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/api/import_lists.py) | 219 | 32 |
| [backend/mangarr/config.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/config.py) | 63 | 13 |
| [backend/mangarr/jobs/tasks.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/jobs/tasks.py) | 2615 | 82 |
| [backend/mangarr/library/importer.py](https://github.com/KiranTheRam/mangarr/blob/09238b58405fb94a5c753405475e30a3b4d5cb78/backend/mangarr/library/importer.py) | 368 | 26 |

Review each route's permissions, validation, service calls, settings defaults and UI binding before acceptance. No runtime testing.
