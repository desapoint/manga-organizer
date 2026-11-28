using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    // A scanned group represents one work only when its confirmed links agree.
    private const string SharedSeriesId = """
        CASE WHEN COUNT(DISTINCT linked.id) = 1 THEN MIN(linked.id) END
        """;

    private const string GroupSeriesKeys = "f.series_key IN (SELECT value FROM json_each($seriesKeys))";
    private const string GroupWorkKeys = "SELECT value FROM json_each($workKeys)";

    private static void BindSeriesGroup(SqliteCommand command, string seriesKey, bool published)
    {
        var group = new List<string>();
        command.Parameters.AddWithValue("$series", seriesKey);
        if (published)
        {
            command.CommandText = """
                WITH groups AS (
                    SELECT f.series_key,
                """ + " " + SharedSeriesId + """
                 AS metadata_id
                    FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
                    JOIN content_metadata cm ON cm.content_id = f.content_id
                    JOIN series_metadata linked ON linked.id = cm.metadata_id AND
                """ + " " + LinkedSeriesScope("f", "linked") + " AND " + ReviewedLocation("f", "cm") + """

                    WHERE f.root_id = $root AND folder.kind IN ('library', 'both')
                    GROUP BY f.series_key
                )
                SELECT peer.series_key FROM groups current JOIN groups peer ON peer.metadata_id = current.metadata_id
                WHERE current.series_key = $series ORDER BY peer.series_key;
                """;
            using var rows = command.ExecuteReader();
            while (rows.Read()) group.Add(rows.GetString(0));
        }

        if (group.Count == 0) group.Add(seriesKey);
        command.Parameters["$series"].Value = group[0];
        command.Parameters.AddWithValue("$seriesKeys", JsonSerializer.Serialize(group));
    }

    private static string[] ReadSeriesGroup(SqliteCommand command) =>
        JsonSerializer.Deserialize<string[]>((string)command.Parameters["$seriesKeys"].Value!)!;

    private const string RelatedChapterState = """
        LEFT JOIN (
            SELECT volume, chapter, MAX(completed) AS completed FROM chapter_completion
            WHERE owner_id = $owner AND work_key IN (
        """ + " " + GroupWorkKeys + """
            )
            GROUP BY volume, chapter
        ) related_status ON related_status.volume = coverage.volume AND related_status.chapter = coverage.chapter
        """;

    private const string RelatedPreference = """
        SELECT MIN(p.content_id) FROM preferred_chapter_release p
        WHERE p.owner_id = $owner AND p.volume = $volume AND p.chapter = $chapter
            AND p.work_key IN (
        """ + " " + GroupWorkKeys + ")";
}
