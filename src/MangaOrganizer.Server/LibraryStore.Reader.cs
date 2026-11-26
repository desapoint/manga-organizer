using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record ReaderSource(Guid RootId, string SeriesKey, string RootPath, string RelativePath, long Size,
    DateTimeOffset ModifiedAt, byte[] Hash);
internal sealed record ReadingProgress(int PageIndex, int Revision, bool Completed);
internal sealed record ReadingChange(Guid UpdateId, int BaseRevision, int PageIndex, bool? Completed);
internal sealed record ReadingResult(ReadingProgress Progress, bool Conflict);
internal sealed record ResumeReading(string ContentId, string Title, Guid RootId, string SeriesKey, bool HasCover,
    DateTimeOffset? LastScannedAt, int PageIndex, int PageCount);

internal sealed partial class LibraryStore
{
    public IReadOnlyList<ReaderSource> ReadSources(string contentId)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT r.id, f.series_key, folder.path, f.relative_path, f.size, f.modified_at, c.sha256
            FROM library_file f JOIN library_content c ON c.id = f.content_id
            JOIN library_folder folder ON folder.root_id = f.root_id JOIN library_root r ON r.id = f.root_id
            WHERE c.id = $id AND f.extension IN ('cbz', 'zip')
            ORDER BY r.state = 'complete' DESC, r.last_scanned_at DESC, f.relative_path;
            """;
        command.Parameters.AddWithValue("$id", contentId);
        var sources = new List<ReaderSource>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            sources.Add(new(rows.GetGuid(0), rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetInt64(4),
                rows.GetDateTimeOffset(5), rows.GetFieldValue<byte[]>(6)));
        }
        return sources;
    }

    public ReadingProgress ReadProgress(Guid owner, string content)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$content", content);
        return ReadProgress(command);
    }

    public ReadingResult SaveProgress(Guid owner, string content, ReadingChange change, int pageCount)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$content", content);
        var progress = ReadProgress(command);

        command.Parameters.AddWithValue("$update", change.UpdateId);
        command.CommandText = """
            SELECT 1 FROM reading_progress_event WHERE owner_id = $owner AND content_id = $content AND update_id = $update;
            """;
        if (command.ExecuteScalar() is not null) return new(progress, false);
        if (change.BaseRevision != progress.Revision) return new(progress, true);

        var next = new ReadingProgress(change.PageIndex, checked(progress.Revision + 1), change.Completed ?? progress.Completed);
        command.Parameters.AddWithValue("$position", next.PageIndex);
        command.Parameters.AddWithValue("$pageCount", pageCount);
        command.Parameters.AddWithValue("$revision", next.Revision);
        command.Parameters.AddWithValue("$complete", next.Completed);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        command.CommandText = """
            INSERT INTO reading_progress VALUES ($owner, $content, $position, $pageCount, $revision, $complete, $now)
            ON CONFLICT(owner_id, content_id) DO UPDATE SET page_index = excluded.page_index, page_count = excluded.page_count,
                revision = excluded.revision, completed = excluded.completed, updated_at = excluded.updated_at;
            INSERT INTO reading_progress_event VALUES ($owner, $content, $update, $revision);
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
        return new(next, false);
    }

    public ResumeReading? ReadResume(Guid owner)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT p.content_id, COALESCE(m.title, f.series_title, f.relative_path), f.root_id, f.series_key,
                EXISTS(SELECT 1 FROM library_series_cover cover WHERE cover.root_id = f.root_id AND cover.series_key = f.series_key),
                r.last_scanned_at, p.page_index, p.page_count
            FROM reading_progress p JOIN library_file f ON f.content_id = p.content_id
            JOIN library_folder folder ON folder.root_id = f.root_id JOIN library_root r ON r.id = f.root_id
            LEFT JOIN content_metadata cm ON cm.content_id = f.content_id LEFT JOIN series_metadata m ON m.id = cm.metadata_id
            WHERE p.owner_id = $owner AND p.completed = 0 AND f.extension IN ('cbz', 'zip') AND r.state <> 'unavailable'
            ORDER BY p.updated_at DESC, r.state = 'complete' DESC, f.relative_path LIMIT 1;
            """;
        command.Parameters.AddWithValue("$owner", owner);
        using var row = command.ExecuteReader();
        return row.Read() ? new(row.GetString(0), row.GetString(1), row.GetGuid(2), row.GetString(3), row.GetBoolean(4),
            row.IsDBNull(5) ? null : row.GetDateTimeOffset(5), row.GetInt32(6), row.GetInt32(7)) : null;
    }

    private static ReadingProgress ReadProgress(SqliteCommand command)
    {
        command.CommandText = "SELECT page_index, revision, completed FROM reading_progress WHERE owner_id = $owner AND content_id = $content;";
        using var rows = command.ExecuteReader();
        return rows.Read() ? new(rows.GetInt32(0), rows.GetInt32(1), rows.GetBoolean(2)) : new(0, 0, false);
    }
}
