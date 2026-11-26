using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record SeriesMetadata(string Title, string Kind, string? AlternateTitle, string? Summary, string? Status,
    int? TotalChapters, int? TotalVolumes, int? ProviderId);
internal sealed record SeriesChapter(string? Volume, string Chapter);
internal sealed record SeriesDetails(SeriesMetadata? Metadata, int AvailableChapters, IReadOnlyList<string> Volumes, IReadOnlyList<SeriesChapter> Chapters);

internal sealed partial class LibraryStore
{
    private const string MetadataIdForGroup = """
        COALESCE(
            (SELECT binding.metadata_id FROM series_metadata_binding binding WHERE binding.root_id = $root AND binding.series_key = $series),
            (SELECT CASE WHEN COUNT(DISTINCT cm.metadata_id) = 1 THEN MIN(cm.metadata_id) END
                FROM library_file f JOIN content_metadata cm ON cm.content_id = f.content_id
                WHERE f.root_id = $root AND f.series_key = $series)
        )
        """;

    private const string GroupCoverage = """
        SELECT f.content_id, c.volume, c.chapter FROM library_file f JOIN content_chapter c ON c.content_id = f.content_id
        WHERE f.root_id = $root AND f.series_key = $series
        UNION
        SELECT f.content_id, COALESCE(f.volume_hint, ''), f.chapter_hint FROM library_file f
        WHERE f.root_id = $root AND f.series_key = $series AND f.content_id IS NOT NULL AND f.chapter_hint IS NOT NULL
            AND NOT EXISTS (SELECT 1 FROM content_chapter c WHERE c.content_id = f.content_id)
        """;

    internal static void BackfillBookNumbers(SqliteConnection database, SqliteCommand command)
    {
        database.CreateFunction<string, string?>("volume_hint_for", path => BookNumbers.FromPath(path).Volume, isDeterministic: true);
        database.CreateFunction<string, string?>("chapter_hint_for", path => BookNumbers.FromPath(path).Chapter, isDeterministic: true);
        command.CommandText = "UPDATE library_file SET volume_hint = volume_hint_for(relative_path), chapter_hint = chapter_hint_for(relative_path);";
        command.ExecuteNonQuery();
    }

    public SeriesDetails? ReadSeriesDetails(Guid root, string seriesKey)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, seriesKey);
        if (!GroupExists(command)) return null;

        command.CommandText = """
            SELECT title, kind, alternate_title, summary, status, total_chapters, total_volumes, provider_id
            FROM series_metadata WHERE id =
            """ + " " + MetadataIdForGroup;
        SeriesMetadata? metadata = null;
        using (var rows = command.ExecuteReader())
        {
            if (rows.Read())
            {
                metadata = new(rows.GetString(0), rows.GetString(1), ReadText(rows, 2), ReadText(rows, 3), ReadText(rows, 4),
                    ReadInt(rows, 5), ReadInt(rows, 6), ReadInt(rows, 7));
            }
        }

        command.CommandText = "SELECT DISTINCT volume, chapter FROM (" + GroupCoverage + ");";
        var chapters = new List<SeriesChapter>();
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                var volume = rows.GetString(0);
                chapters.Add(new(volume.Length == 0 ? null : volume, rows.GetString(1)));
            }
        }

        command.CommandText = """
            SELECT DISTINCT volume_hint FROM library_file WHERE root_id = $root AND series_key = $series AND volume_hint IS NOT NULL;
            """;
        var volumes = chapters.Select(chapter => chapter.Volume).OfType<string>().ToHashSet(StringComparer.Ordinal);
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read()) volumes.Add(rows.GetString(0));
        }

        return new(metadata, chapters.Count, volumes.OrderBy(Numeric).Take(500).ToArray(),
            chapters.OrderBy(chapter => Numeric(chapter.Volume)).ThenBy(chapter => Numeric(chapter.Chapter)).Take(500).ToArray());
    }

    public bool SaveMetadata(Guid root, string seriesKey, SeriesMetadata metadata)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, seriesKey);
        if (!GroupExists(command)) return false;

        command.CommandText = "SELECT " + MetadataIdForGroup;
        var id = command.ExecuteScalar() as string ?? Guid.NewGuid().ToString("N");
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", metadata.Title);
        command.Parameters.AddWithValue("$kind", metadata.Kind);
        command.Parameters.AddWithValue("$alternate", (object?)metadata.AlternateTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", (object?)metadata.Summary ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (object?)metadata.Status ?? DBNull.Value);
        command.Parameters.AddWithValue("$chapters", (object?)metadata.TotalChapters ?? DBNull.Value);
        command.Parameters.AddWithValue("$volumes", (object?)metadata.TotalVolumes ?? DBNull.Value);
        command.Parameters.AddWithValue("$provider", (object?)metadata.ProviderId ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        command.CommandText = """
            INSERT INTO series_metadata(id, series_key, title, alternate_title, kind, summary, status, total_chapters, total_volumes, provider_id, updated_at)
                VALUES ($id, $series, $title, $alternate, $kind, $summary, $status, $chapters, $volumes, $provider, $now)
            ON CONFLICT(id) DO UPDATE SET title = excluded.title, kind = excluded.kind, alternate_title = excluded.alternate_title,
                summary = excluded.summary, status = excluded.status, total_chapters = excluded.total_chapters,
                total_volumes = excluded.total_volumes, provider_id = excluded.provider_id, updated_at = excluded.updated_at;
            INSERT INTO series_metadata_binding(root_id, series_key, metadata_id) VALUES ($root, $series, $id)
            ON CONFLICT(root_id, series_key) DO UPDATE SET metadata_id = excluded.metadata_id;
            INSERT INTO content_metadata(content_id, metadata_id)
                SELECT DISTINCT content_id, $id FROM library_file
                WHERE root_id = $root AND series_key = $series AND content_id IS NOT NULL
            ON CONFLICT(content_id) DO UPDATE SET metadata_id = excluded.metadata_id;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public bool SaveCoverage(Guid root, string seriesKey, string contentId, string volume, IReadOnlyList<string> chapters)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, seriesKey);
        command.Parameters.AddWithValue("$content", contentId);
        if (!ContentInGroup(command)) return false;

        command.CommandText = "DELETE FROM content_chapter WHERE content_id = $content;";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT OR IGNORE INTO content_chapter(content_id, volume, chapter) VALUES ($content, $volume, $chapter);";
        command.Parameters.AddWithValue("$volume", volume);
        var chapterParameter = command.Parameters.Add("$chapter", SqliteType.Text);
        foreach (var chapter in chapters)
        {
            chapterParameter.Value = chapter;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return true;
    }

    public IReadOnlyList<SeriesChapter>? ReadCoverage(Guid root, string seriesKey, string contentId)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, seriesKey);
        command.Parameters.AddWithValue("$content", contentId);
        if (!ContentInGroup(command)) return null;

        command.CommandText = "SELECT volume, chapter FROM content_chapter WHERE content_id = $content;";
        var result = new List<SeriesChapter>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var volume = rows.GetString(0);
            result.Add(new(volume.Length == 0 ? null : volume, rows.GetString(1)));
        }
        return result.OrderBy(chapter => Numeric(chapter.Chapter)).ToArray();
    }

    private static SqliteCommand GroupCommand(SqliteConnection database, SqliteTransaction transaction, Guid root, string seriesKey)
    {
        var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$root", root);
        command.Parameters.AddWithValue("$series", seriesKey);
        return command;
    }

    private static bool GroupExists(SqliteCommand command)
    {
        command.CommandText = """
            SELECT 1 FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            WHERE f.root_id = $root AND f.series_key = $series LIMIT 1;
            """;
        return command.ExecuteScalar() is not null;
    }

    private static bool ContentInGroup(SqliteCommand command)
    {
        command.CommandText = """
            SELECT 1 FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            WHERE f.root_id = $root AND f.series_key = $series AND f.content_id = $content LIMIT 1;
            """;
        return command.ExecuteScalar() is not null;
    }

    private static decimal Numeric(string? value) => decimal.TryParse(value, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
    private static string? ReadText(SqliteDataReader rows, int index) => rows.IsDBNull(index) ? null : rows.GetString(index);
    private static int? ReadInt(SqliteDataReader rows, int index) => rows.IsDBNull(index) ? null : rows.GetInt32(index);

    private static void StageKnownSeries(SqliteConnection database)
    {
        using var command = database.CreateCommand();
        command.CommandText = """
            UPDATE scan_file AS f SET series_key = m.series_key
            FROM scan_hash h JOIN library_content c ON c.sha256 = h.sha256
            JOIN content_metadata cm ON cm.content_id = c.id JOIN series_metadata m ON m.id = cm.metadata_id
            WHERE f.relative_path = h.relative_path;
            """;
        command.ExecuteNonQuery();
    }
}
