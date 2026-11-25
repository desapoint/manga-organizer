# KK — second-pass settings and operational routes

Pinned [revision](https://github.com/Kha-kis/manga-arr/tree/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef). Source contents fetched for the files below; patterns enumerate declarations and candidate fields only, not verified runtime semantics. Scope: 7/7 files. See [signals](index/KK-pass2.csv).

| File | Lines | Signals |
|---|---:|---:|
| [app/routers/settings_.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/settings_.py) | 726 | 50 |
| [app/routers/download_clients.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/download_clients.py) | 726 | 88 |
| [app/routers/custom_formats.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/custom_formats.py) | 470 | 37 |
| [app/routers/release_profiles.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/release_profiles.py) | 248 | 22 |
| [app/routers/delay_profiles.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/delay_profiles.py) | 162 | 19 |
| [app/routers/language_profiles.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/language_profiles.py) | 184 | 18 |
| [app/routers/quality_profiles.py](https://github.com/Kha-kis/manga-arr/blob/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef/app/routers/quality_profiles.py) | 178 | 25 |

Review each route's permissions, validation, service calls, settings defaults and UI binding before acceptance. No runtime testing.
