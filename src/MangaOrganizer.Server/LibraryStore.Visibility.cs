namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    // A title guess or another file's metadata does not confirm this file's association.
    private const string ConfirmedSeries = """
        EXISTS (SELECT 1 FROM content_metadata link JOIN series_metadata metadata ON metadata.id = link.metadata_id
            WHERE link.content_id = f.content_id AND metadata.series_key = f.series_key)
        """;

    private const string LibraryVisibility = """
         AND EXISTS (SELECT 1 FROM library_folder role WHERE role.root_id = f.root_id AND role.kind IN ('library', 'both'))
         AND
        """ + " " + ConfirmedSeries;

    private const string MissingMetadata = """
        NOT EXISTS (SELECT 1 FROM series_metadata_binding binding WHERE binding.root_id = f.root_id AND binding.series_key = f.series_key)
        AND 1 <> (
            SELECT COUNT(DISTINCT link.metadata_id) FROM library_file peer JOIN content_metadata link ON link.content_id = peer.content_id
            JOIN series_metadata metadata ON metadata.id = link.metadata_id AND metadata.series_key = f.series_key
            WHERE peer.root_id = f.root_id AND peer.series_key = f.series_key)
        """;

    private const string MissingCover = """
        NOT EXISTS (SELECT 1 FROM library_series_cover cover WHERE cover.root_id = f.root_id AND cover.series_key = f.series_key)
        AND NOT EXISTS (SELECT 1 FROM library_content content JOIN artwork.archive_cover cover ON cover.archive_sha = content.sha256
            WHERE content.id = f.content_id)
        """;

    private static string CatalogVisibility(string? view) => view switch
    {
        "library" => " " + LibraryVisibility,
        "attention" => " AND (NOT (" + ConfirmedSeries + ") OR (" + MissingMetadata + ") OR (" + MissingCover + "))",
        "unassociated" => " AND f.content_id IS NOT NULL AND NOT (" + ConfirmedSeries + ")",
        "association" => " AND f.series_title IS NOT NULL AND NOT (" + ConfirmedSeries + ")",
        "series" => " AND f.series_title IS NULL AND NOT (" + ConfirmedSeries + ")",
        "metadata" => " AND (" + MissingMetadata + ")",
        "cover" => " AND (" + MissingCover + ")",
        _ => "",
    };

    private static string SeriesVisibility(bool published) => published ? " " + LibraryVisibility : "";

    private static string ScopedGroupCoverage(bool published) => published
        ? "SELECT coverage.* FROM (" + GroupCoverage + """
            ) coverage WHERE coverage.content_id IN (
                SELECT f.content_id FROM library_file f WHERE f.root_id = $root AND f.series_key = $series
            """ + SeriesVisibility(true) + ")"
        : GroupCoverage;
}
