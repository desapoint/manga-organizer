# Series detail page redesign

The interactive mockup illustrates the library detail page. Open the HTML locally to try its filters and volume map. Its sample data and actions are illustrative. Adapt the layout to the information a reader needs at each screen size. [Interactive mockup](mockups/manga-series-v3.html)

## Page layout and reading

- [ ] Keep the dedicated Library series URL and a clear way back to Library. Keep Import intake's expandable lists and inspector for association and file operations.
- [ ] Lead with the cached cover, series title, short synopsis and reading action when available. Show useful metadata near the title, such as publication type or status. Keep the page usable without artwork or metadata.
- [x] Give **More info** a keyboard-accessible dialog for the full synopsis, alternate title, available creator credits, publication facts, genres, tags, metadata source and library location. Keep metadata lookup and editing available on the series page. [Metadata roadmap](ROADMAP.md#metadata-enhancements)
- [x] Show **Continue reading** when the primary server has an unfinished position for this series. Use the real file and page position, and show **Start reading** when the series has a readable starting point and no saved progress.
- [ ] Use the shared Tailwind theme, Lucide icons, buttons, styled selects and responsive navigation. Keep the cover and primary action easy to reach on phones. Wrap filters and action groups, keep 44px touch targets, and test long Unicode titles and paths at narrow widths in both themes.

## Collection and reading progress

- [ ] Show chapters available versus released and chapters read versus owned with separately labeled totals. When available, also show chapters read versus released and storage for the indexed files. State which locations count toward the storage figure.
- [ ] Show owned volumes identified by local files or coverage links. Add complete, partial and missing volume counts and a selectable volume map when a reliable release manifest specifies the expected chapters for each volume. A volume selection should expose its coverage and open the related chapter list.
- [ ] Keep unknown release totals, unresolved associations and confirmed gaps distinct. An aggregate chapter or volume total cannot establish which issues are missing. Treat filename-derived hints as suggestions until confirmed by the catalog, and count verified chapter coverage once across release variants.

## Chapters, volumes and files

- [x] Open and resume an indexed volume archive even when chapter assignments are unresolved. **View files** applies the selected volume filter before pagination. Keep known release counts visible when no corresponding chapters or volumes have been indexed, with reading controls only for supported local releases. [Volume navigation](TOOLS-AND-INTAKE.md#volume-navigation)

- [ ] Group related information into Chapters, Volumes and Files sections or tabs. Put secondary metadata in More info. Keep file playback, metadata editing and reading status in Library. Link to Tools for coverage assignment and reviewed file operations as those modules take ownership.
- [ ] Support chapter search and release-order sorting, with filters for volume, availability, read state and chapter type when supported by the data. Keep pagination for large series. Show fractional chapters, prologues and named extras in the correct release order with stable identities.
- [ ] Show available formats and release attributes, with explicit chapter completion and preferred content already available from PRs #26 and #30. Extend the picker to canonical chapter IDs, strict/flexible preferences and fast release choice. [Release selection](READING-CONTINUITY.md#release-selection)
- [ ] Keep Files useful for inspection and reading. Show per-file thumbnails and identities, variants, unresolved coverage and real scan state. Route file management to Tools. Use verified catalog data for collection-health figures.

## Data and verification

The expanded series work includes canonical chapter counts, independent volume membership, release evidence and durable duplicate decisions. Group these changes by the dependencies in the backlog. [Implementation backlog](EXPANDED-BACKLOG.md)

- [ ] Reuse the existing cover, inventory, catalog, progress and managed-operation contracts. Add versioned migrations for any new metadata fields, release ordering or chapter-level progress records.
- [ ] Verify layouts and dialog keyboard behavior, missing metadata and covers, unknown release totals, file variants, reading handoff, and that reviewed file actions still revalidate and preserve recovery. Scope any release-manifest source or expanded metadata ingestion separately.
