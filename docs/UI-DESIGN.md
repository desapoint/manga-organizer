# INK//SHELF WebUI design

Manga Organizer uses an editorial, dark-first interface. The `INK//SHELF` direction uses oversized page headings, close tracking, small section labels, readable technical paths, subtle borders and rounded surfaces. Artwork comes from the collection. Decorative motion respects the client's reduced-motion setting.

## Theme and shell

Tailwind holds the theme tokens. Ink uses `#101015` canvas, `#141319` sidebar, `#1B1A22` surfaces, `#24222C` overlays, `#302D39` borders, `#F6F3FA` primary text and `#A6A2B6` secondary text. Violet `#BDA5FA` is the default accent. Mint `#96E2C5` and coral `#FFA5A2` are alternatives. Warning uses `#F4C478` and danger uses `#FF7D92`. Paper uses warm surfaces and charcoal text with darker semantic accents for legibility. Store appearance preferences in the current browser.

Use a persistent 232px desktop sidebar with an icon-only compact mode and accessible labels. On phones, use bottom navigation for Overview, Library and Import intake. More contains Settings and sign-out. Respect safe areas and maintain 44px touch targets for primary actions. Keep destinations, status labels and outline focus consistent on both layouts.

## Functional screens

- Library cover cards and series rows open a dedicated series page with the title, cover, and paginated files. The page has a stable link and keeps Library selected in the navigation. Import intake keeps its expandable rows and inspector for association and file operations.

- **Overview** shows counts from configured roots, indexed file locations, folders needing review and actual scan activity. Add continue-reading and recent titles after the reader can supply those records.
- **Library** offers grouped series suggestions and a paginated file inventory. Keep filesystem paths, formats, sizes, dates and exact-byte match signals available in the file view and each group's expanded file list. Search and cover controls belong alongside the grouped view as their backend data becomes available.
- **Import intake** uses inventory and an inspector side by side on wide screens, with an inspector above the mobile navigation when an entry is selected. Show the source path, title suggestion or unresolved state, and actual file hash and format. A title suggestion keeps chapter coverage pending. [Association requirements](CATALOG-ASSOCIATION.md)
- **Settings** groups folder roles and scan status, presentation preferences and the primary server's ID. A single folder can serve both the library and intake. The folder browser retains suggested locations, mounts, breadcrumbs, path filtering and the explicit selection action.
- **First launch and sign-in** share a centered layout and editorial heading. Preserve the one-time setup link, password requirements and optional server folder picker.

## Screens as supporting features land

| Area | Interaction | Backend evidence |
| --- | --- | --- |
| Cover browsing | Lazy-load 2:3 covers with a fallback for unavailable images. Opening a card reveals the indexed series files. Add search, sorting and reading status as the server supports them. | File-backed `cover.png` or first readable archive page and real series groups. |
| Series details | Lead with the cover, title and reading action. Organize Chapters, Volumes and Files, and place secondary metadata in More info. [Design checklist](SERIES-DETAIL-TODO.md) | Reviewed series and chapter links, release evidence and progress. |
| Reader | Use an almost-black workspace, hideable toolbar, right-to-left paging, continuous scroll and a visible page position. Navigate actual files or linked chapters. | Approved CBZ and image ZIP reader with validated progress through the primary. |
| Advanced import | Review title and chapter links, inspect source and destination, preview the operation, confirm when required and show the result. | Qualified storage, explicit or unambiguous association, and revalidation and recovery. |
| Integrity review | Lead with the two covers and file summaries. Follow the series mockup's coverage panel with a shared-chapter count, an overlap bar and a compact numbered chapter map. Equal-height bands show the files that cover each chapter. Use one legend and a shared color for single-chapter files, plus a separate legend with distinct colors for volumes and bundles. Hover, focus, or tap a tile for its details or a legend item to highlight its file. Keep paths and hashes available on demand and show a dry-run plan before any managed change. | Verified physical identity and linked chapter coverage. |
| Downloads | Show search results, queue state, retries and import review. Display the companion's real connection state. | Separately managed Suwayomi and approved acquisition support. |

Use shared React controls and accessibility primitives. Prefer named variants, visible focus, composable content, and Lucide icons with accessible names. Keep pagination, long Unicode titles and paths, narrow widths and unavailable states in acceptance checks. [WebUI requirements](ARCHITECTURE.md#webui-design-requirements) · [Reader roadmap](ROADMAP.md#webui-mvp)
