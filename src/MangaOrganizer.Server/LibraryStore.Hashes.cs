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
        query.CommandText = "SELECT relative_path, size, modified_at FROM scan_file ORDER BY relative_path;";
        using var insert = database.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO scan_hash VALUES ($path, $sha256);";
        var path = insert.Parameters.Add("$path", SqliteType.Text);
        var sha256 = insert.Parameters.Add("$sha256", SqliteType.Blob);
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
                var digest = file is null ? null : LibraryFileHasher.Read(file, rows.GetInt64(1), rows.GetDateTimeOffset(2).UtcDateTime,
                    buffer, cancellationToken, scan.ReadBytes);
                path.Value = relativePath;
                sha256.Value = (object?)digest ?? DBNull.Value;
                insert.ExecuteNonQuery();
                scan.FinishedFile(digest is not null);
            }
        }

        transaction.Commit();
    }
}
