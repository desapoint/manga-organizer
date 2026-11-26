# Series detail page redesign

The interactive mockup illustrates the library detail page. Open the HTML locally to try its filters and volume map. Its sample data and actions are illustrative. Adapt the layout to the information a reader needs at each screen size. [Interactive mockup](mockups/manga-series-v3.html)

## Page layout and reading

- [ ] Keep the dedicated Library series URL and a clear way back to Library. Keep Import intake's expandable lists and inspector for association and file operations.
- [ ] Lead with the cached cover, series title, short synopsis and reading action when available. Show useful metadata near the title, such as publication type or status. Keep the page usable without artwork or metadata.
- [ ] Give **More info** an accessible dialog for the full synopsis, alternate titles, creator credits, publication year and status, original language, genres, tags, metadata source and library location as those records become available. Keep metadata lookup and editing easy to find. Return keyboard focus to the invoking control when the dialog closes. [Metadata roadmap](ROADMAP.md#metadata-enhancements)
- [ ] Show **Continue reading** when the primary server has an unfinished position for this series. Use the real file and page position, and show **Start reading** when the series has a readable starting point and no saved progress.
- [ ] Use the shared Tailwind theme, Lucide icons, buttons, styled selects and responsive navigation. Keep the cover and primary action easy to reach on phones. Wrap filters and action groups, keep 44px touch targets, and test long Unicode titles and paths at narrow widths in both themes.

## Collection and reading progress

- [ ] Show chapters available versus released and chapters read versus owned with separately labeled totals. When available, also show chapters read versus released and storage for the indexed files. State which locations count toward the storage figure.
- [ ] Show owned volumes identified by local files or coverage links. Add complete, partial and missing volume counts and a selectable volume map when a reliable release manifest specifies the expected chapters for each volume. A volume selection should expose its coverage and open the related chapter list.
- [ ] Keep unknown release totals, unresolved associations and confirmed gaps distinct. An aggregate chapter or volume total cannot establish which issues are missing. Treat filename-derived hints as suggestions until confirmed by the catalog, and count verified chapter coverage once across release variants.

## Chapters, volumes and files

- [ ] Group related information into Chapters, Volumes and Files sections or tabs. Put secondary metadata in More info. Keep file playback, manual coverage and reviewed rename or move actions available from Files on storage that permits them.
- [ ] Support chapter search and release-order sorting, with filters for volume, availability, read state and chapter type when supported by the data. Keep pagination for large series. Show fractional chapters, prologues and named extras in the correct release order with stable identities.
- [ ] Show available formats and variants, and let readers open a playable release. Add explicit read and unread controls once the primary server can record chapter-level state across devices, including when a chapter has several files or shares a volume file with other chapters.
- [ ] Keep the Files view useful for administration. Show per-file thumbnails and identities, variants, unresolved coverage, real scan state and management actions. Use verified catalog data for any collection-health figures.

## Data and verification

- [ ] Reuse the existing cover, inventory, catalog, progress and managed-operation contracts. Add versioned migrations for any new metadata fields, release ordering or chapter-level progress records.
- [ ] Verify layouts and dialog keyboard behavior, missing metadata and covers, unknown release totals, file variants, reading handoff, and that reviewed file actions still revalidate and preserve recovery. Scope any release-manifest source or expanded metadata ingestion separately.
