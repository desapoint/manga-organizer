using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    public byte[]? ReadCover(Guid rootId, string seriesKey)
    {
        using var database = OpenCoverDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT folder.path, cover.relative_path, cover.size, cover.modified_at, cover.archive_entry, cover.sha256, image.data
            FROM library_series_cover cover JOIN library_folder folder ON folder.root_id = cover.root_id
            LEFT JOIN artwork.image image ON image.sha256 = cover.sha256
            WHERE cover.root_id = $id AND cover.series_key = $seriesKey;
            """;
        command.Parameters.AddWithValue("$id", rootId);
        command.Parameters.AddWithValue("$seriesKey", seriesKey);
        using var rows = command.ExecuteReader();
        if (!rows.Read()) return null;
        if (!rows.IsDBNull(6)) return rows.GetFieldValue<byte[]>(6);

        // Populate older series cover records on their first request after an upgrade.
        var path = rows.GetString(0);
        var source = new CoverSource(rows.GetString(1), rows.GetInt64(2), rows.GetDateTimeOffset(3),
            rows.IsDBNull(4) ? null : rows.GetString(4), rows.GetFieldValue<byte[]>(5));
        rows.Close();
        var cache = new CoverCache(database);
        return CoverArt.Read(path, source) is { } original && cache.Store(original) is { } digest ? cache.Read(digest) : null;
    }

    public byte[]? ReadFileCover(Guid rootId, string fileId)
    {
        using var database = OpenCoverDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT image.data FROM library_file file JOIN library_folder folder ON folder.root_id = file.root_id
            JOIN library_content content ON content.id = file.content_id
            JOIN artwork.archive_cover cover ON cover.archive_sha = content.sha256
            JOIN artwork.image image ON image.sha256 = cover.image_sha
            WHERE file.root_id = $root AND file.id = $file;
            """;
        command.Parameters.AddWithValue("$root", rootId);
        command.Parameters.AddWithValue("$file", fileId);
        return command.ExecuteScalar() as byte[];
    }

    private SqliteConnection OpenCoverDatabase()
    {
        var database = OpenDatabase();
        try
        {
            CoverCache.Attach(database, dataDirectory);
            return database;
        }
        catch
        {
            database.Dispose();
            throw;
        }
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
            SELECT f.series_key, f.directory, d.unambiguous, f.relative_path, f.extension, f.size, f.modified_at, f.sha256, f.series_title
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

        using var rememberSidecar = database.CreateCommand();
        rememberSidecar.Transaction = transaction;
        rememberSidecar.CommandText = "INSERT INTO scan_sidecar VALUES ($series, $path, $image);";
        var candidateSeries = rememberSidecar.Parameters.Add("$series", SqliteType.Text);
        var candidatePath = rememberSidecar.Parameters.Add("$path", SqliteType.Text);
        var candidateImage = rememberSidecar.Parameters.Add("$image", SqliteType.Blob);

        var cache = new CoverCache(database, transaction);
        string? currentKey = null;
        var directories = new HashSet<string>(StringComparer.Ordinal);
        CoverSource? lowestArchiveCover = null;
        CoverOrder lowestOrder = default;
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
                    lowestArchiveCover = null;
                }

                if (rows.GetBoolean(2)) directories.Add(rows.GetString(1));
                if (ArchivePages.Supports(rows.GetString(4)) && !rows.IsDBNull(7))
                {
                    var archiveDigest = rows.GetFieldValue<byte[]>(7);
                    var relativePath = rows.GetString(3);
                    var fileSize = rows.GetInt64(5);
                    var modifiedAt = rows.GetDateTimeOffset(6);
                    var cover = cache.FindArchive(archiveDigest, relativePath, fileSize, modifiedAt);
                    if (cover is null)
                    {
                        cover = CoverArt.FindArchiveCover(cache, root, relativePath, fileSize, modifiedAt, cancellationToken);
                        if (cover is not null) cache.RememberArchive(archiveDigest, cover);
                    }
                    if (cover is not null)
                    {
                        var order = CoverOrder.ForFile(relativePath, rows.IsDBNull(8) ? null : rows.GetString(8));
                        if (lowestArchiveCover is null || order.CompareTo(lowestOrder) < 0)
                        {
                            lowestArchiveCover = cover;
                            lowestOrder = order;
                        }
                    }
                }
            }
        }

        PublishCandidate();
        transaction.Commit();

        void PublishCandidate()
        {
            if (currentKey is null) return;

            CoverSource? firstSidecar = null;
            candidateSeries.Value = currentKey;
            foreach (var sidecar in directories.SelectMany(directory => CoverArt.FindSidecars(root, directory, cache)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                firstSidecar ??= sidecar;
                candidatePath.Value = sidecar.RelativePath;
                candidateImage.Value = sidecar.Sha256;
                rememberSidecar.ExecuteNonQuery();
            }

            var source = firstSidecar ?? lowestArchiveCover;
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
