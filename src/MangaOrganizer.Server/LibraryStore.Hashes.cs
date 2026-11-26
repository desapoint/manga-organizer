using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    private static void StageHashes(SqliteConnection database, string root, ScanProgress scan, CancellationToken cancellationToken)
    {
        scan.StartPhase("hashing");
        using var transaction = database.BeginTransaction(deferred: true);
        using var query = database.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = "SELECT relative_path, size, modified_at, extension, volume_hint, chapter_hint FROM scan_file ORDER BY relative_path;";
        using var insert = database.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO scan_hash VALUES ($path, $sha256, $chapters, $source);";
        var path = insert.Parameters.Add("$path", SqliteType.Text);
        var sha256 = insert.Parameters.Add("$sha256", SqliteType.Blob);
        var chapters = insert.Parameters.Add("$chapters", SqliteType.Text);
        var source = insert.Parameters.Add("$source", SqliteType.Text);
        insert.Prepare();

        var buffer = new byte[64 * 1024];
        using (var rows = query.ExecuteReader())
        {
            while (rows.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = rows.GetString(0);
                scan.BeginFile(relativePath);
                var file = MediaFiles.Resolve(root, relativePath);
                var volumePath = ArchivePages.Supports(rows.GetString(3)) && !rows.IsDBNull(4) && rows.IsDBNull(5) ? relativePath : null;
                var result = file is null ? null : LibraryFileHasher.ReadForIndex(file, rows.GetInt64(1), rows.GetDateTimeOffset(2).UtcDateTime,
                    buffer, cancellationToken, scan.ReadBytes, volumePath);
                path.Value = relativePath;
                sha256.Value = (object?)result?.Digest ?? DBNull.Value;
                chapters.Value = result?.Coverage is { } coverage ? JsonSerializer.Serialize(coverage.Chapters) : DBNull.Value;
                source.Value = (object?)result?.Coverage?.Source ?? DBNull.Value;
                insert.ExecuteNonQuery();
                scan.FinishedFile(result is not null);
            }
        }

        transaction.Commit();
    }
}
