# KV — second-pass settings and operational routes

Pinned [revision](https://github.com/Kareadita/Kavita/tree/7fcf1085a5355a8c592d17adcfbb28b69276ae40). Source contents fetched for the files below; patterns enumerate declarations and candidate fields only, not verified runtime semantics. Scope: 6/6 files. See [signals](index/KV-pass2.csv).

| File | Lines | Signals |
|---|---:|---:|
| [Kavita.Server/Controllers/SettingsController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/SettingsController.cs) | 386 | 52 |
| [Kavita.Server/Controllers/LibraryController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/LibraryController.cs) | 790 | 76 |
| [Kavita.Server/Controllers/ReadingProfileController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/ReadingProfileController.cs) | 279 | 34 |
| [Kavita.Server/Controllers/OPDSController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/OPDSController.cs) | 747 | 61 |
| [Kavita.Server/Controllers/KoreaderController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/KoreaderController.cs) | 86 | 7 |
| [Kavita.Server/Controllers/TachiyomiController.cs](https://github.com/Kareadita/Kavita/blob/7fcf1085a5355a8c592d17adcfbb28b69276ae40/Kavita.Server/Controllers/TachiyomiController.cs) | 53 | 5 |

Review each route's permissions, validation, service calls, settings defaults and UI binding before acceptance. No runtime testing.
