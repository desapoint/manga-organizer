# Repository instructions

Read README, REFERENCES, docs/UPSTREAM-AUDIT.md, docs/FEATURE-MATRIX.md, docs/FEATURE-SELECTION.csv docs/EXTENSIONS.md and docs/FILE-ORGANIZATION.md first. Documentation-first. Do not silently approve Pending features. Required outcomes are Windows, Unraid/Docker WebUI Mihon-compatible extension downloading, file organization and duplicate finding/handling. Keep architecture and dependency choices provisional until approved. Trace claims to upstream revisions; distinguish branch/release/paid/planned/unknown. No copying before license review. One authoritative backend; never overwrite existing media by default. Add tests for approved features and record actual platform qualification. Never commit secrets or personal media.

For feature research, use docs/source-audit/README.md, the pinned per-project reports, and ATOMIC-FEATURES.csv as the current detailed inventory. Treat extracted declarations and tree candidates as unverified behavior; preserve evidence levels, pinned links and pending decisions. The owner directed the primary-server foundation to begin. Check the remaining evidence and approved scope before relying on upstream behavior or enabling a qualified deployment or file operation.

Server-hosted reading and cross-device progress are required. One Windows or Linux primary holds the main library and validates its reading progress. Android, Windows and Linux devices act as clients. Optional device uploads go through the primary's verified import path. Optional subscriptions to other servers keep separate identities and credentials. See docs/REMOTE-READING.md.

Use docs/IMPLEMENTATION-DECISIONS.md and docs/FEATURE-SELECTION.csv for the approved scope. Check the owner notes when a row groups several capabilities. Deferred features require a later owner decision.

WebUI changes must follow the shared design system. Use Tailwind CSS and its theme tokens for styling, Lucide for UI icons, and reusable React components for repeated controls and layouts. Give shared components semantic markup, named style variants, composable content, and native props where applicable. Keep visible navigation labels, keyboard focus and responsive layouts. See the WebUI section in docs/ARCHITECTURE.md.

Use the shared accessible select component for styled dropdowns. Keep action buttons at their intrinsic size, align them with adjacent content, and wrap action groups on narrow screens.

Follow the INK//SHELF design direction in docs/UI-DESIGN.md. Keep Manga Organizer as the application name. Use the shared responsive shell, editorial page headers, dark-first Tailwind palette and warm paper mode. Display real server data and expose functional destinations only. Keep file inventory accessible alongside series and cover views.

Allow one folder to serve as the library, import intake, or both. A dual-purpose folder shares one root ID, inventory and scan across both views. Keep storage mutation permissions separate from folder roles. Show series groups before individual files in the library and import inventory, including folder-based grouping for numbered files and title-based grouping for chapter and volume suffixes. Keep ambiguous entries visible and preserve original paths and content identities for review.

The first usable WebUI reader must show cover art from the media files. Honor a folder cover first, then use the first supported archive page. Build the WebUI MVP in stacked pull requests. Branch from the previous PR after its checks pass, and leave the stack open for the owner to merge in order.

The MVP requires live scan progress, searchable local files and basic manga and doujin metadata, series and chapter availability, reading completion counts, one functional right-to-left reader, reviewed file moves and renames, and versioned SQLite migrations with a runnable command and verification job. Keep acquisition, downloads and complex metadata scraping outside the MVP. Track deliverables in docs/ROADMAP.md.

Add new SQLite schema changes as numbered SQL migrations in src/MangaOrganizer.Server/Migrations and register them in DatabaseMigrations. Keep applied migrations immutable. Test fresh installation and supported legacy upgrades with the packaged migration command.

File organization and deduplication are core features (F69/F70), not optional reader extras. Read-only discovery first; never remove a file based on title, size, weak fingerprint or similar-looking pages. Protect hardlinks, alternate editions, progress and unique chapter/page coverage. No automatic permanent deletion without explicit owner policy; preview/revalidate/journal/restore all approved mutations. See docs/FILE-ORGANIZATION.md for requirements and acceptance gates.

Audit completion is explicitly requested. Consult docs/source-audit/COMPLETION-LEDGER.md and PASS3.md. Close module behavior, release/license and runtime gates with recorded evidence. Foundation smoke tests verify the development build and retain the target qualification gates.
