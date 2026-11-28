using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record SeriesFileReview(string FileId, string ContentId)
{
    public static bool IsValid(SeriesFileReview[]? files) => files is null || files.All(file =>
        file is not null && IsId(file.FileId) && IsId(file.ContentId));

    private static bool IsId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigit);
}

internal sealed partial class LibraryStore
{
    private const string ReviewedFiles = """
        SELECT f.id, f.content_id
        FROM json_each($reviewFiles) reviewed
        JOIN library_file f ON f.id = lower(reviewed.value ->> '$.FileId')
            AND f.content_id = lower(reviewed.value ->> '$.ContentId')
        WHERE f.root_id = $root AND f.series_key = $series
        """;

    private static bool BindReviewedFiles(SqliteCommand command, SeriesFileReview[]? files)
    {
        if (files is null) return true;
        command.Parameters.AddWithValue("$reviewFiles", JsonSerializer.Serialize(files));
        if (files.Length == 0) return true;
        command.CommandText = ReviewedFiles + " LIMIT 1;";
        return command.ExecuteScalar() is not null;
    }

    private static void AttachReviewedFiles(SqliteCommand command)
    {
        command.CommandText = """
            WITH eligible AS (
            """ + ReviewedFiles + """
            )
            INSERT INTO content_metadata(content_id, metadata_id, reviewed_locations_only)
            SELECT DISTINCT content_id, $id, 1 FROM eligible WHERE 1
            ON CONFLICT(content_id) DO NOTHING;

            WITH eligible AS (
            """ + ReviewedFiles + """
            )
            INSERT OR IGNORE INTO file_association_review(file_id, content_id)
            SELECT eligible.id, eligible.content_id FROM eligible JOIN content_metadata link USING(content_id)
            WHERE link.metadata_id = $id;
            """;
        command.ExecuteNonQuery();
    }

    private static void StageRenamedReviews(SqliteCommand command)
    {
        // A single missing reviewed location with one new path for the same bytes is a rename.
        // Existing unreviewed copies and ambiguous moves remain pending.
        command.CommandText = """
            CREATE TEMP TABLE scan_renamed_review AS
            WITH vanished AS (
                SELECT previous.content_id FROM library_file previous
                JOIN file_association_review review ON review.file_id = previous.id AND review.content_id = previous.content_id
                WHERE previous.root_id = $id AND NOT EXISTS (
                    SELECT 1 FROM scan_file candidate WHERE candidate.relative_path = previous.relative_path)
                GROUP BY previous.content_id HAVING COUNT(*) = 1
            )
            SELECT MIN(h.relative_path) AS relative_path, c.id AS content_id FROM vanished
            JOIN library_content c ON c.id = vanished.content_id
            JOIN scan_hash h ON h.sha256 = c.sha256
            WHERE NOT EXISTS (SELECT 1 FROM library_file previous WHERE previous.root_id = $id AND previous.relative_path = h.relative_path)
            GROUP BY c.id HAVING COUNT(*) = 1;
            """;
        command.ExecuteNonQuery();
    }
}
