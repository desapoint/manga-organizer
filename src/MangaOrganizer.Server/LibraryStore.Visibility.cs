namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    private static string LinkedSeriesScope(string file, string metadata) => $$"""
        ({{metadata}}.series_key = {{file}}.series_key OR EXISTS (
            SELECT 1 FROM series_metadata_binding alias WHERE alias.root_id = {{file}}.root_id
                AND alias.series_key = {{file}}.series_key AND alias.metadata_id = {{metadata}}.id
        ))
        """;

    // A title guess or another file's metadata does not confirm this file's association.
    private static string ConfirmedSeries => """
        EXISTS (SELECT 1 FROM content_metadata link JOIN series_metadata metadata ON metadata.id = link.metadata_id
            WHERE link.content_id = f.content_id AND
        """ + " " + LinkedSeriesScope("f", "metadata") + " AND " + ReviewedLocation("f", "link") + ")";

    private static string ReviewedLocation(string file, string link) => $$"""
        ({{link}}.reviewed_locations_only = 0 OR EXISTS (
            SELECT 1 FROM file_association_review review WHERE review.file_id = {{file}}.id AND review.content_id = {{file}}.content_id
        ))
        """;

    private static string LibraryVisibility => """
         AND EXISTS (SELECT 1 FROM library_folder role WHERE role.root_id = f.root_id AND role.kind IN ('library', 'both'))
         AND
        """ + " " + ConfirmedSeries;

    private static string MissingMetadata => """
        NOT EXISTS (SELECT 1 FROM series_metadata_binding binding WHERE binding.root_id = f.root_id AND binding.series_key = f.series_key)
        AND 1 <> (
            SELECT COUNT(DISTINCT link.metadata_id) FROM library_file peer JOIN content_metadata link ON link.content_id = peer.content_id
            JOIN series_metadata metadata ON metadata.id = link.metadata_id
            WHERE peer.root_id = f.root_id AND peer.series_key = f.series_key AND
        """ + " " + LinkedSeriesScope("peer", "metadata") + ")";

    private const string MissingCover = """
        NOT EXISTS (SELECT 1 FROM library_series_cover cover WHERE cover.root_id = f.root_id AND cover.series_key = f.series_key)
        AND NOT EXISTS (SELECT 1 FROM library_content content JOIN artwork.archive_cover cover ON cover.archive_sha = content.sha256
            WHERE content.id = f.content_id)
        """;

    private const string CanConfirmIntakeProposal = """
        NOT EXISTS(SELECT 1 FROM series_metadata_binding binding WHERE binding.root_id = f.root_id AND binding.series_key = f.series_key)
        AND NOT EXISTS(SELECT 1 FROM library_file peer JOIN content_metadata linked ON linked.content_id = peer.content_id
            WHERE peer.root_id = f.root_id AND peer.series_key = f.series_key)
        """;

    private const string CloseAniListProposal = """
        EXISTS(SELECT 1 FROM metadata_suggestion proposal WHERE proposal.series_key = f.series_key
            AND (SELECT COUNT(*) FROM json_each(proposal.candidates) candidate
                WHERE json_type(candidate.value, '$.MatchingTitle') = 'text') = 1)
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
        "close" => " AND (" + CanConfirmIntakeProposal + ") AND (" + CloseAniListProposal + ")",
        _ => "",
    };

    private static string SeriesVisibility(bool published) => published ? " " + LibraryVisibility : "";

    private static string ScopedGroupCoverage(bool published) => published
        ? "SELECT coverage.* FROM (" + GroupCoverage + """
            ) coverage WHERE coverage.content_id IN (
                SELECT f.content_id FROM library_file f WHERE f.root_id = $root AND
            """ + " " + GroupSeriesKeys + SeriesVisibility(true) + ")"
        : GroupCoverage;
}
