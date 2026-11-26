using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    private sealed record ArchiveCandidate(string RelativePath, long Size, DateTimeOffset ModifiedAt);

    public RasterImage? ReadCover(Guid rootId, string seriesKey)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT folder.path, cover.relative_path, cover.size, cover.modified_at, cover.archive_entry, cover.sha256
            FROM library_series_cover cover JOIN library_folder folder ON folder.root_id = cover.root_id
            WHERE cover.root_id = $id AND cover.series_key = $seriesKey;
            """;
        command.Parameters.AddWithValue("$id", rootId);
        command.Parameters.AddWithValue("$seriesKey", seriesKey);
        using var rows = command.ExecuteReader();
        if (!rows.Read()) return null;

        var path = rows.GetString(0);
        var source = new CoverSource(rows.GetString(1), rows.GetInt64(2), rows.GetDateTimeOffset(3),
            rows.IsDBNull(4) ? null : rows.GetString(4), rows.GetFieldValue<byte[]>(5));
        rows.Close();
        return CoverArt.Read(path, source);
    }

    private static void StageCovers(SqliteConnection database, string root, CancellationToken cancellationToken)
    {
        using var transaction = database.BeginTransaction(deferred: true);
        using var query = database.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            WITH directories AS (
                SELECT directory, COUNT(DISTINCT series_key) = 1 AS unambiguous FROM scan_file GROUP BY directory
            )
            SELECT f.series_key, f.directory, d.unambiguous, f.relative_path, f.extension, f.size, f.modified_at
            FROM scan_file f JOIN directories d USING(directory)
            ORDER BY f.series_key, f.relative_path;
            """;
        using var insert = database.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO scan_cover VALUES ($seriesKey, $path, $size, $modified, $entry, $sha256);";
        var key = insert.Parameters.Add("$seriesKey", SqliteType.Text);
        var path = insert.Parameters.Add("$path", SqliteType.Text);
        var size = insert.Parameters.Add("$size", SqliteType.Integer);
        var modified = insert.Parameters.Add("$modified", SqliteType.Text);
        var entry = insert.Parameters.Add("$entry", SqliteType.Text);
        var sha256 = insert.Parameters.Add("$sha256", SqliteType.Blob);

        string? currentKey = null;
        var directories = new HashSet<string>(StringComparer.Ordinal);
        var archives = new List<ArchiveCandidate>();
        using (var rows = query.ExecuteReader())
        {
            while (rows.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var seriesKey = rows.GetString(0);
                if (currentKey != seriesKey)
                {
                    PublishCandidate();
                    currentKey = seriesKey;
                    directories.Clear();
                    archives.Clear();
                }

                if (rows.GetBoolean(2) && directories.Count < 16) directories.Add(rows.GetString(1));
                if (archives.Count < 12 && ArchivePages.Supports(rows.GetString(4)))
                {
                    archives.Add(new(rows.GetString(3), rows.GetInt64(5), rows.GetDateTimeOffset(6)));
                }
            }
        }

        PublishCandidate();
        transaction.Commit();

        void PublishCandidate()
        {
            if (currentKey is null) return;

            var source = directories.Select(directory => CoverArt.FindSidecar(root, directory)).FirstOrDefault(source => source is not null)
                ?? archives.Select(archive => CoverArt.FindFirstPage(root, archive.RelativePath, archive.Size, archive.ModifiedAt, cancellationToken))
                    .FirstOrDefault(source => source is not null);
            if (source is null) return;

            key.Value = currentKey;
            path.Value = source.RelativePath;
            size.Value = source.Size;
            modified.Value = source.ModifiedAt;
            entry.Value = (object?)source.ArchiveEntry ?? DBNull.Value;
            sha256.Value = source.Sha256;
            insert.ExecuteNonQuery();
        }
    }
}
