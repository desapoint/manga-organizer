using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record ClearedArtwork(long Images, long ImageBytes);

internal sealed partial class LibraryStore
{
    public ClearedArtwork? ClearArtworkCache()
    {
        using var database = OpenCoverDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM library_root WHERE state IN ('queued', 'scanning'))
                OR EXISTS(SELECT 1 FROM file_operation WHERE state IN ('applying', 'restoring'));
            """;
        if ((long)command.ExecuteScalar()! != 0) return null;

        command.CommandText = "SELECT COUNT(*), COALESCE(SUM(length(data)), 0) FROM artwork.image;";
        ClearedArtwork cleared;
        using (var rows = command.ExecuteReader())
        {
            rows.Read();
            cleared = new(rows.GetInt64(0), rows.GetInt64(1));
        }
        command.CommandText = """
            DELETE FROM artwork.archive_cover;
            DELETE FROM artwork.archive_without_pages;
            DELETE FROM library_sidecar_stamp;
            DELETE FROM artwork.folder_cover;
            DELETE FROM artwork.image;
            PRAGMA artwork.incremental_vacuum;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
        return cleared;
    }

    public SeriesCoverPicture? ReadCover(Guid rootId, string seriesKey, bool highResolution = false)
    {
        using var database = OpenCoverDatabase();
        using var command = database.CreateCommand();
        command.Parameters.AddWithValue("$root", rootId);
        command.Parameters.AddWithValue("$series", seriesKey);
        if (GroupExists(command) && ReadPreferredCover(command) is { } preferred)
        {
            return highResolution && preferred.ProviderUrl is not null &&
                MetadataAgent.CoverUrl(ReadMetadata(command)?.ProviderDetails?.CoverLargeUrl) is { } larger
                ? preferred with { ProviderUrl = larger } : preferred;
        }

        command.CommandText = """
            SELECT folder.path, cover.relative_path, cover.size, cover.modified_at, cover.archive_entry, cover.sha256, image.data
            FROM library_series_cover cover JOIN library_folder folder ON folder.root_id = cover.root_id
            LEFT JOIN artwork.image image ON image.sha256 = cover.sha256
            WHERE cover.root_id = $root AND cover.series_key = $series;
            """;
        using var rows = command.ExecuteReader();
        if (!rows.Read()) return null;
        if (!rows.IsDBNull(6)) return new(rows.GetFieldValue<byte[]>(6), null);

        // Populate older series cover records on their first request after an upgrade.
        var path = rows.GetString(0);
        var source = new CoverSource(rows.GetString(1), rows.GetInt64(2), rows.GetDateTimeOffset(3),
            rows.IsDBNull(4) ? null : rows.GetString(4), rows.GetFieldValue<byte[]>(5));
        rows.Close();
        var cache = new CoverCache(database);
        return CoverArt.Read(path, source) is { } original && cache.Store(original) is { } digest
            && cache.Read(digest) is { } data ? new(data, null) : null;
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

    private static void StageCovers(SqliteConnection database, SqliteConnection artwork, LibraryRoot root, ScanProgress scan,
        CancellationToken cancellationToken)
    {
        using var snapshot = database.CreateCommand();
        snapshot.CommandText = """
            DELETE FROM scan_cover;
            DELETE FROM scan_sidecar;
            CREATE TEMP TABLE scan_cover_input AS
            WITH directories AS (
                SELECT directory, COUNT(DISTINCT series_key) = 1 AS unambiguous FROM scan_file GROUP BY directory
            )
            SELECT f.series_key, f.directory, d.unambiguous, f.relative_path, f.extension, f.size, f.modified_at,
                h.sha256 AS archive_sha, f.series_title, h.stamp, cover.entry, image.sha256 AS image_sha,
                no_pages.archive_sha IS NOT NULL AS without_pages
            FROM scan_file f JOIN directories d USING(directory)
            LEFT JOIN scan_hash h ON h.relative_path = f.relative_path
            LEFT JOIN artwork.archive_cover cover ON cover.archive_sha = h.sha256
            LEFT JOIN artwork.image image ON image.sha256 = cover.image_sha
            LEFT JOIN artwork.archive_without_pages no_pages ON no_pages.archive_sha = h.sha256;
            """;
        snapshot.ExecuteNonQuery();

        // Keep extraction and encoding outside any transaction on the persistent databases.
        using var transaction = database.BeginTransaction(deferred: true);
        using var query = database.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = "SELECT COUNT(DISTINCT series_key) FROM scan_file;";
        scan.BeginCovers((long)query.ExecuteScalar()!);
        query.CommandText = """
            SELECT series_key, directory, unambiguous, relative_path, extension, size, modified_at, archive_sha, series_title,
                stamp, entry, image_sha, without_pages
            FROM scan_cover_input ORDER BY series_key, relative_path;
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
        rememberSidecar.CommandText = "INSERT INTO scan_sidecar VALUES ($series, $path, $image, $stamp);";
        var candidateSeries = rememberSidecar.Parameters.Add("$series", SqliteType.Text);
        var candidatePath = rememberSidecar.Parameters.Add("$path", SqliteType.Text);
        var candidateImage = rememberSidecar.Parameters.Add("$image", SqliteType.Blob);
        var candidateStamp = rememberSidecar.Parameters.Add("$stamp", SqliteType.Text);

        using var findSidecar = artwork.CreateCommand();
        findSidecar.CommandText = """
            SELECT cover.image_sha FROM library_sidecar_stamp saved
            JOIN artwork.folder_cover cover USING(root_id, relative_path)
            JOIN artwork.image image ON image.sha256 = cover.image_sha
            WHERE saved.root_id = $root AND saved.relative_path = $path AND saved.stamp = $stamp;
            """;
        findSidecar.Parameters.AddWithValue("$root", root.Id);
        var sidecarPath = findSidecar.Parameters.Add("$path", SqliteType.Text);
        var sidecarStamp = findSidecar.Parameters.Add("$stamp", SqliteType.Text);

        var cache = new CoverCache(artwork);
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
                    scan.BeginFile(relativePath);
                    var fileSize = rows.GetInt64(5);
                    var modifiedAt = rows.GetDateTimeOffset(6);
                    if (rows.GetBoolean(12) && rows.IsDBNull(11)) continue;
                    var cover = rows.IsDBNull(11)
                        ? cache.FindArchive(archiveDigest, relativePath, fileSize, modifiedAt)
                        : new CoverSource(relativePath, fileSize, modifiedAt, rows.GetString(10), rows.GetFieldValue<byte[]>(11));
                    if (cover is null && CoverArt.InspectArchiveCover(cache, root.Path, relativePath, fileSize, modifiedAt,
                        ReadText(rows, 9), cancellationToken) is { } inspected)
                    {
                        cover = inspected.Cover;
                        if (cover is not null) cache.RememberArchive(archiveDigest, cover);
                        else cache.RememberArchiveWithoutPages(archiveDigest);
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
        snapshot.CommandText = "DROP TABLE scan_cover_input;";
        snapshot.ExecuteNonQuery();

        byte[]? ReuseSidecar(string relativePath, string stamp)
        {
            if (scan.Full) return null;
            sidecarPath.Value = relativePath;
            sidecarStamp.Value = stamp;
            return findSidecar.ExecuteScalar() as byte[];
        }

        void PublishCandidate()
        {
            if (currentKey is null) return;

            CoverSource? firstSidecar = null;
            candidateSeries.Value = currentKey;
            foreach (var sidecar in directories.SelectMany(directory => CoverArt.FindSidecars(root.Path, directory, cache, ReuseSidecar)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                firstSidecar ??= sidecar;
                candidatePath.Value = sidecar.RelativePath;
                candidateImage.Value = sidecar.Sha256;
                candidateStamp.Value = (object?)sidecar.Stamp ?? DBNull.Value;
                rememberSidecar.ExecuteNonQuery();
            }

            var source = firstSidecar ?? lowestArchiveCover;
            scan.CheckedCover(source is not null);
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
