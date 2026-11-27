using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record MetadataProposal(MetadataMatch Match, string? MatchingTitle);
internal sealed record MetadataQueueItem(Guid RootId, string SeriesKey, string Title, bool HasCover, bool Queued, string? Revision,
    IReadOnlyList<MetadataProposal>? Candidates, string? Message, bool ConflictingMetadata, bool PagePriority);
internal sealed record MetadataWork(string SeriesKey, string Title, string Revision);
internal sealed record MetadataQueueLocation(Guid RootId, string SeriesKey, bool AlreadyQueued = false);

internal sealed partial class LibraryStore
{
    private const string UnresolvedSeries = """
        SELECT f.root_id, f.series_key, MIN(f.series_title) AS title,
            COUNT(DISTINCT cm.metadata_id) > 1 OR MAX(cm.metadata_id IS NOT NULL AND linked.id IS NULL) AS conflict
        FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
        LEFT JOIN series_metadata_binding binding ON binding.root_id = f.root_id AND binding.series_key = f.series_key
        LEFT JOIN content_metadata cm ON cm.content_id = f.content_id
        LEFT JOIN series_metadata linked ON linked.id = cm.metadata_id AND linked.series_key = f.series_key
        WHERE ($queueRoot IS NULL OR f.root_id = $queueRoot) AND ($queueSeries IS NULL OR f.series_key = $queueSeries)
        GROUP BY f.root_id, f.series_key
        HAVING MIN(f.series_title) IS NOT NULL AND COUNT(f.content_id) > 0
            AND MAX(binding.metadata_id) IS NULL
            AND (COUNT(DISTINCT linked.id) <> 1 OR MAX(cm.metadata_id IS NOT NULL AND linked.id IS NULL))
        """;

    private static void QueueArrivingSeries(SqliteCommand command)
    {
        command.CommandText = """
            INSERT INTO metadata_suggestion(series_key, title, revision, queued, requested_at)
            SELECT s.series_key, MIN(s.series_title), lower(hex(randomblob(16))), 1, unixepoch()
            FROM scan_file s JOIN scan_hash h ON h.relative_path = s.relative_path
            LEFT JOIN library_content content ON content.sha256 = h.sha256
            LEFT JOIN content_metadata cm ON cm.content_id = content.id
            WHERE s.series_title IS NOT NULL AND h.sha256 IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM library_file old WHERE old.root_id = $id AND old.series_key = s.series_key
                    AND old.content_id IS NOT NULL)
                AND NOT EXISTS (SELECT 1 FROM series_metadata_binding binding WHERE binding.root_id = $id AND binding.series_key = s.series_key)
            GROUP BY s.series_key
            HAVING COUNT(DISTINCT cm.metadata_id) = 0
            ON CONFLICT(series_key) DO NOTHING;
            """;
        command.ExecuteNonQuery();
    }

    public CatalogPage<MetadataQueueItem> ReadMetadataQueue(Guid? root, string search, int offset, int limit)
    {
        using var database = OpenDatabase();
        RegisterTitleSearch(database);
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$queueRoot", (object?)root ?? DBNull.Value);
        command.Parameters.AddWithValue("$queueSeries", DBNull.Value);
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$offset", offset);
        command.Parameters.AddWithValue("$limit", limit);
        var filtered = "WITH unresolved AS (" + UnresolvedSeries + "), filtered AS (SELECT * FROM unresolved WHERE $search = '' OR title_contains(title, $search)) ";
        command.CommandText = filtered + "SELECT COUNT(*) FROM filtered;";
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = filtered + """
            SELECT f.root_id, f.series_key, f.title,
                EXISTS(SELECT 1 FROM library_series_cover c WHERE c.root_id = f.root_id AND c.series_key = f.series_key),
                COALESCE(s.queued, 0), s.revision, s.candidates, s.message, f.conflict, COALESCE(s.priority, 0)
            FROM filtered f LEFT JOIN metadata_suggestion s ON s.series_key = f.series_key
            ORDER BY f.title COLLATE NOCASE, f.root_id, f.series_key LIMIT $limit OFFSET $offset;
            """;
        var items = new List<MetadataQueueItem>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var candidates = ReadText(rows, 6) is { } json ? JsonSerializer.Deserialize<MetadataProposal[]>(json) : null;
            items.Add(new(rows.GetGuid(0), rows.GetString(1), rows.GetString(2), rows.GetBoolean(3), rows.GetBoolean(4),
                ReadText(rows, 5), candidates, ReadText(rows, 7), rows.GetBoolean(8), rows.GetBoolean(9)));
        }
        return new(total, items);
    }

    public int RefreshMetadataSuggestions(IReadOnlyList<MetadataQueueLocation> visible)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        var root = command.Parameters.Add("$queueRoot", SqliteType.Text);
        var series = command.Parameters.Add("$queueSeries", SqliteType.Text);
        var alreadyQueued = command.Parameters.Add("$alreadyQueued", SqliteType.Integer);
        command.CommandText = "WITH unresolved AS (" + UnresolvedSeries + """
            )
            INSERT INTO metadata_suggestion(series_key, title, revision, queued, requested_at, priority)
            SELECT unresolved.series_key, unresolved.title, lower(hex(randomblob(16))), 1, unixepoch(), 1
            FROM unresolved LEFT JOIN metadata_suggestion current USING (series_key)
            WHERE NOT conflict AND (NOT $alreadyQueued OR current.queued = 1)
            ON CONFLICT(series_key) DO UPDATE SET
                title = CASE WHEN metadata_suggestion.queued THEN metadata_suggestion.title ELSE excluded.title END,
                requested_at = CASE WHEN metadata_suggestion.queued THEN metadata_suggestion.requested_at ELSE excluded.requested_at END,
                retry_at = CASE WHEN metadata_suggestion.queued THEN metadata_suggestion.retry_at END,
                message = CASE WHEN metadata_suggestion.queued THEN metadata_suggestion.message END,
                queued = 1, priority = 1
            WHERE metadata_suggestion.queued = 0 OR metadata_suggestion.priority = 0;
            """;
        var changed = 0;
        foreach (var item in visible)
        {
            root.Value = item.RootId;
            series.Value = item.SeriesKey;
            alreadyQueued.Value = item.AlreadyQueued;
            changed += command.ExecuteNonQuery();
        }
        transaction.Commit();
        return changed;
    }

    public MetadataWork? NextMetadataSuggestion()
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT s.series_key, s.title, s.revision FROM metadata_suggestion s
            WHERE s.queued = 1
                AND NOT EXISTS (SELECT 1 FROM metadata_suggestion delayed WHERE delayed.queued = 1 AND delayed.retry_at > $now)
                AND EXISTS (SELECT 1 FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
                    WHERE f.series_key = s.series_key AND NOT EXISTS(
                        SELECT 1 FROM series_metadata_binding binding WHERE binding.root_id = f.root_id AND binding.series_key = s.series_key)
                    GROUP BY f.root_id HAVING COUNT(f.content_id) > 0 AND COUNT(DISTINCT (
                        SELECT cm.metadata_id FROM content_metadata cm WHERE cm.content_id = f.content_id)) = 0)
            ORDER BY s.priority DESC, s.requested_at, s.series_key LIMIT 1;
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var rows = command.ExecuteReader();
        return rows.Read() ? new(rows.GetString(0), rows.GetString(1), rows.GetString(2)) : null;
    }

    public void FinishMetadataSuggestion(MetadataWork work, IReadOnlyList<MetadataProposal>? candidates, string? message, DateTimeOffset? retryAt = null)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            UPDATE metadata_suggestion SET candidates = COALESCE($candidates, candidates), message = $message,
                revision = CASE WHEN $candidates IS NULL THEN revision ELSE lower(hex(randomblob(16))) END,
                queued = $queued, retry_at = $retry, priority = CASE WHEN $queued THEN priority ELSE 0 END
            WHERE series_key = $series AND revision = $revision AND queued = 1;
            """;
        command.Parameters.AddWithValue("$series", work.SeriesKey);
        command.Parameters.AddWithValue("$revision", work.Revision);
        command.Parameters.AddWithValue("$candidates", candidates is null ? DBNull.Value : JsonSerializer.Serialize(candidates));
        command.Parameters.AddWithValue("$message", (object?)message ?? DBNull.Value);
        command.Parameters.AddWithValue("$queued", retryAt.HasValue);
        command.Parameters.AddWithValue("$retry", (object?)retryAt?.ToUnixTimeMilliseconds() ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public SeriesMetadata? ConfirmMetadataSuggestion(Guid root, string series, string revision, int providerId)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, series);
        if (!GroupExists(command) || GroupHasActiveIntakeTransfer(command) || ReadMetadata(command) is not null) return null;
        command.CommandText = """
            SELECT 1 FROM library_file f JOIN content_metadata cm ON cm.content_id = f.content_id
            WHERE f.root_id = $root AND f.series_key = $series LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null) return null;
        command.CommandText = "SELECT candidates FROM metadata_suggestion WHERE series_key = $series AND revision = $revision;";
        command.Parameters.AddWithValue("$revision", revision);
        if (command.ExecuteScalar() is not string json) return null;
        var match = JsonSerializer.Deserialize<MetadataProposal[]>(json)?.FirstOrDefault(item => item.Match.ProviderId == providerId)?.Match;
        if (match is null) return null;
        var saved = WriteMetadata(command, match.ToMetadata());
        transaction.Commit();
        return saved;
    }
}
