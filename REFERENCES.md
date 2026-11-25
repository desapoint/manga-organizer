# Upstream reference registry

Reviewed 2026-11-24 (Toronto). Default-branch heads observed during review; documents were retrieved by branch, not independently re-read at each SHA. These are references, not submodules or bundled dependencies.

| ID | Repository | Observed revision | Reported license | Evidence read |
| --- | --- | --- | --- | --- |
| KR | [KiranTheRam/mangarr](https://github.com/KiranTheRam/mangarr) | [main: 09238b58405f](https://github.com/KiranTheRam/mangarr/tree/09238b58405fb94a5c753405475e30a3b4d5cb78) | GPL-3.0 | [README.md](https://github.com/KiranTheRam/mangarr/blob/main/README.md) |
| MK | [OrbitMPGH/Maki](https://github.com/OrbitMPGH/Maki) | [main: 0de45e926f36](https://github.com/OrbitMPGH/Maki/tree/0de45e926f368e23ace4dba04e2211d1de83aadb) | GPL-3.0 | [README.md](https://github.com/OrbitMPGH/Maki/blob/main/README.md), [docs/features.md](https://github.com/OrbitMPGH/Maki/blob/main/docs/features.md) |
| UC | [AngeloSha/uchiyomi](https://github.com/AngeloSha/uchiyomi) | [main: dc5d52212cbe](https://github.com/AngeloSha/uchiyomi/tree/dc5d52212cbe7ad1984fbb29d1490e524f886e0c) | MPL-2.0 | [README.md](https://github.com/AngeloSha/uchiyomi/blob/main/README.md) |
| KK | [Kha-kis/manga-arr](https://github.com/Kha-kis/manga-arr) | [master: d9353fe8629b](https://github.com/Kha-kis/manga-arr/tree/d9353fe8629b90dfc4e44a1ba7215d7ffd233eef) | AGPL-3.0-only | [README.md](https://github.com/Kha-kis/manga-arr/blob/master/README.md), [docs/sonarr-parity.md](https://github.com/Kha-kis/manga-arr/blob/master/docs/sonarr-parity.md) |
| KV | [Kareadita/Kavita](https://github.com/Kareadita/Kavita) | [develop: 7fcf1085a535](https://github.com/Kareadita/Kavita/tree/7fcf1085a5355a8c592d17adcfbb28b69276ae40) | GPL-3.0 | [README.md](https://github.com/Kareadita/Kavita/blob/develop/README.md) |
| SW | [Suwayomi/Suwayomi-Server](https://github.com/Suwayomi/Suwayomi-Server) | [master: 4081b05f0245](https://github.com/Suwayomi/Suwayomi-Server/tree/4081b05f02456ec099c85127dfa5253e968d48a7) | MPL-2.0 | [README.md](https://github.com/Suwayomi/Suwayomi-Server/blob/master/README.md) |
| MH | [mihonapp/mihon](https://github.com/mihonapp/mihon) | [main: f8e30a860860](https://github.com/mihonapp/mihon/tree/f8e30a86086020aaee1e89a9d015373b1a4ff026) | Apache-2.0 | [README.md](https://github.com/mihonapp/mihon/blob/main/README.md) |

## Further audit references (not yet reviewed)

- MK: [documentation](https://orbitmpgh.github.io/Maki/), [Windows](https://github.com/OrbitMPGH/Maki/blob/main/docs/windows.md), [third-party notices](https://github.com/OrbitMPGH/Maki/blob/main/THIRD_PARTY_NOTICES.md).
- UC: [usage](https://github.com/AngeloSha/uchiyomi/blob/main/docs/USAGE.md), [desktop](https://github.com/AngeloSha/uchiyomi/blob/main/docs/DESKTOP.md), [API](https://github.com/AngeloSha/uchiyomi/blob/main/docs/api.md), [comparison](https://github.com/AngeloSha/uchiyomi/blob/main/docs/COMPARISON.md).
- KK: [qualification](https://github.com/Kha-kis/manga-arr/blob/master/docs/release-qualification.md), [deployment](https://github.com/Kha-kis/manga-arr/blob/master/docs/deployment.md).
- KV: [wiki](https://wiki.kavitareader.com/), [Kavita+](https://wiki.kavitareader.com/kavita+), [Kobo](https://wiki.kavitareader.com/guides/features/kobo-sync/).
- SW: [server wiki](https://github.com/Suwayomi/Suwayomi-Server/wiki), [WebUI](https://github.com/Suwayomi/Suwayomi-WebUI), [Docker](https://github.com/Suwayomi/docker-tachidesk), [Mihon connector extension](https://github.com/Suwayomi/tachiyomi-extension).
- MH: [website and usage](https://mihon.app/), [changelog](https://mihon.app/changelogs/), source/extension interfaces and backup schemas.

## Reuse policy

Compare behavior first. Review license compatibility, attribution, redistribution, and model/data/plugin terms before copying anything. GPL/AGPL/MPL/Apache obligations differ. Maki reports MangaBaka-original data under CC BY-NC-SA 4.0 and some enhancement models as noncommercial. Respect Kavita+ subscriptions, source access controls, rate limits and terms. Third-party extensions are separately licensed and trusted by the operator. The owner selected [Apache-2.0](LICENSE) for original Manga Organizer code.

## Pass 7 license and deployment evidence

All seven pinned root license texts rechecked; see [pass 7](docs/source-audit/PASS7.md), [asset rights register](docs/source-audit/LICENSE-ASSET-REGISTER.csv) and [deployment qualification](docs/source-audit/DEPLOYMENT-QUALIFICATION.md). No project license chosen, no upstream code reused; separate data, weights, native dependency and service rights still open.
