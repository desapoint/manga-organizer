using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    private static void StageHashes(SqliteConnection database, LibraryRoot root, ScanProgress scan, CancellationToken cancellationToken)
    {
        scan.StartPhase("hashing");
        // Copy just the reusable metadata before reading files so the catalog is free for interactive writes.
        using var snapshot = database.CreateCommand();
        snapshot.CommandText = """
            CREATE TEMP TABLE scan_cached_hash AS
            SELECT f.relative_path, saved.scan_stamp, content.sha256, saved.scan_chapters, saved.scan_chapter_source
            FROM scan_file f
            JOIN library_file saved ON saved.root_id = $root AND saved.relative_path = f.relative_path
                AND saved.scan_stamp IS NOT NULL AND saved.size = f.size AND saved.modified_at = f.modified_at
                AND saved.volume_hint IS f.volume_hint AND saved.chapter_hint IS f.chapter_hint
            JOIN library_content content ON content.id = saved.content_id
            WHERE $reuse;
            """;
        snapshot.Parameters.AddWithValue("$root", root.Id);
        snapshot.Parameters.AddWithValue("$reuse", !scan.Full);
        snapshot.ExecuteNonQuery();
        using var transaction = database.BeginTransaction(deferred: true);
        using var query = database.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT f.relative_path, f.size, f.modified_at, f.extension, f.volume_hint, f.chapter_hint,
                saved.scan_stamp, saved.sha256, saved.scan_chapters, saved.scan_chapter_source
            FROM scan_file f
            LEFT JOIN scan_cached_hash saved ON saved.relative_path = f.relative_path
            ORDER BY f.relative_path;
            """;
        using var insert = database.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO scan_hash VALUES ($path, $sha256, $chapters, $source, $stamp);";
        var path = insert.Parameters.Add("$path", SqliteType.Text);
        var sha256 = insert.Parameters.Add("$sha256", SqliteType.Blob);
        var chapters = insert.Parameters.Add("$chapters", SqliteType.Text);
        var source = insert.Parameters.Add("$source", SqliteType.Text);
        var stamp = insert.Parameters.Add("$stamp", SqliteType.Text);
        insert.Prepare();

        var buffer = new byte[64 * 1024];
        using (var rows = query.ExecuteReader())
        {
            while (rows.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = rows.GetString(0);
                scan.BeginFile(relativePath);
                var file = MediaFiles.Resolve(root.Path, relativePath);
                var volumePath = ArchivePages.Supports(rows.GetString(3)) && !rows.IsDBNull(4) && rows.IsDBNull(5) ? relativePath : null;
                var cached = rows.IsDBNull(7) ? null : new IndexedDigest(rows.GetFieldValue<byte[]>(7), rows.GetString(6),
                    ReadText(rows, 8), ReadText(rows, 9));
                var result = file is null ? null : LibraryFileHasher.ReadForIndex(file, rows.GetInt64(1), rows.GetDateTimeOffset(2).UtcDateTime,
                    buffer, cancellationToken, scan.ReadBytes, volumePath, cached);
                path.Value = relativePath;
                sha256.Value = (object?)result?.Digest ?? DBNull.Value;
                chapters.Value = (object?)result?.Chapters ?? DBNull.Value;
                source.Value = (object?)result?.ChapterSource ?? DBNull.Value;
                stamp.Value = (object?)result?.Stamp ?? DBNull.Value;
                insert.ExecuteNonQuery();
                scan.FinishedFile(result is not null, result?.Reused == true);
            }
        }

        transaction.Commit();
    }
}
