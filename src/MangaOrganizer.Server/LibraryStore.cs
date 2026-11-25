using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record LibraryRoot(Guid Id, string Path);
internal sealed record LibraryRootStatus(Guid Id, string Path, string State, long FileCount,
    DateTimeOffset? LastAttemptAt, DateTimeOffset? LastScannedAt, long SkippedLinks);
internal sealed record CatalogEntry(string Id, string RelativePath, string Extension, long Size, DateTimeOffset ModifiedAt);
internal sealed record CatalogPage(long Total, IReadOnlyList<CatalogEntry> Items);

internal sealed class LibraryStore
{
    private readonly string dataDirectory;
    private readonly Dictionary<Guid, LibraryRoot> roots = new();

    public LibraryStore(string dataDirectory, IReadOnlyList<string> configuredPaths)
    {
        this.dataDirectory = dataDirectory;
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction();
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS library_root (
                id TEXT PRIMARY KEY,
                path_key TEXT NOT NULL UNIQUE,
                state TEXT NOT NULL DEFAULT 'idle',
                file_count INTEGER NOT NULL DEFAULT 0,
                last_attempt_at TEXT,
                last_scanned_at TEXT,
                skipped_links INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS library_file (
                id TEXT PRIMARY KEY DEFAULT (lower(hex(randomblob(16)))),
                root_id TEXT NOT NULL REFERENCES library_root(id),
                relative_path TEXT NOT NULL,
                extension TEXT NOT NULL,
                size INTEGER NOT NULL,
                modified_at TEXT NOT NULL,
                UNIQUE(root_id, relative_path)
            );
            UPDATE library_root SET state = 'interrupted' WHERE state IN ('queued', 'scanning');
            """;
        command.ExecuteNonQuery();

        command.CommandText = """
            INSERT INTO library_root(id, path_key) VALUES ($id, $path)
            ON CONFLICT(path_key) DO UPDATE SET path_key = excluded.path_key RETURNING id;
            """;
        var rootId = command.Parameters.Add("$id", SqliteType.Text);
        var pathKey = command.Parameters.Add("$path", SqliteType.Text);
        foreach (var path in configuredPaths)
        {
            rootId.Value = Guid.NewGuid();
            pathKey.Value = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
            using var row = command.ExecuteReader();
            row.Read();
            var root = new LibraryRoot(row.GetGuid(0), path);
            roots.Add(root.Id, root);
        }
        transaction.Commit();
    }

    public bool TryGetRoot(Guid id, out LibraryRoot? root) => roots.TryGetValue(id, out root);

    public IReadOnlyList<LibraryRootStatus> ReadRoots()
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT state, file_count, last_attempt_at, last_scanned_at, skipped_links
            FROM library_root WHERE id = $id;
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var result = new List<LibraryRootStatus>(roots.Count);
        foreach (var root in roots.Values)
        {
            id.Value = root.Id;
            using var row = command.ExecuteReader();
            row.Read();
            result.Add(new(root.Id, root.Path, row.GetString(0), row.GetInt64(1),
                row.IsDBNull(2) ? null : row.GetDateTimeOffset(2),
                row.IsDBNull(3) ? null : row.GetDateTimeOffset(3), row.GetInt64(4)));
        }
        return result;
    }

    public CatalogPage ReadFiles(Guid id, int offset, int limit)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT file_count FROM library_root WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = """
            SELECT id, relative_path, extension, size, modified_at
            FROM library_file WHERE root_id = $id ORDER BY relative_path LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<CatalogEntry>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            items.Add(new(rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetInt64(3), rows.GetDateTimeOffset(4)));
        }
        return new(total, items);
    }

    public bool TryQueue(Guid id)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            UPDATE library_root SET state = 'queued', last_attempt_at = $now
            WHERE id = $id AND state NOT IN ('queued', 'scanning');
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        return command.ExecuteNonQuery() == 1;
    }

    public void SetState(Guid id, string state)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "UPDATE library_root SET state = $state WHERE id = $id;";
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Refresh(LibraryRoot root, CancellationToken cancellationToken)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            CREATE TEMP TABLE scan_file (
                relative_path TEXT PRIMARY KEY,
                extension TEXT NOT NULL,
                size INTEGER NOT NULL,
                modified_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        var (count, skippedLinks) = StageFiles(database, root.Path, cancellationToken);
        LibraryScanner.RequireRoot(root.Path);
        cancellationToken.ThrowIfCancellationRequested();

        using var transaction = database.BeginTransaction();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", root.Id);
        command.CommandText = "SELECT file_count FROM library_root WHERE id = $id;";
        var previousCount = (long)command.ExecuteScalar()!;
        if (count == 0 && previousCount > 0)
        {
            command.CommandText = "UPDATE library_root SET state = 'emptyReview' WHERE id = $id;";
        }
        else
        {
            command.CommandText = """
                INSERT INTO library_file(root_id, relative_path, extension, size, modified_at)
                    SELECT $id, relative_path, extension, size, modified_at FROM scan_file WHERE 1
                    ON CONFLICT(root_id, relative_path) DO UPDATE SET
                        extension = excluded.extension, size = excluded.size, modified_at = excluded.modified_at;
                DELETE FROM library_file WHERE root_id = $id AND relative_path NOT IN (SELECT relative_path FROM scan_file);
                UPDATE library_root SET state = 'complete', file_count = $count,
                    last_scanned_at = $now, skipped_links = $skipped WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$count", count);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
            command.Parameters.AddWithValue("$skipped", skippedLinks);
        }
        command.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private static (long Count, long SkippedLinks) StageFiles(SqliteConnection database, string path, CancellationToken cancellationToken)
    {
        // Build the inventory in a temporary table without holding the published catalog's write lock.
        using var transaction = database.BeginTransaction(deferred: true);
        using var insert = database.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO scan_file VALUES ($path, $extension, $size, $modified);";
        var relativePath = insert.Parameters.Add("$path", SqliteType.Text);
        var extension = insert.Parameters.Add("$extension", SqliteType.Text);
        var size = insert.Parameters.Add("$size", SqliteType.Integer);
        var modified = insert.Parameters.Add("$modified", SqliteType.Text);
        insert.Prepare();

        long count = 0;
        var skippedLinks = LibraryScanner.Collect(path, file =>
        {
            relativePath.Value = file.RelativePath;
            extension.Value = file.Extension;
            size.Value = file.Size;
            modified.Value = file.ModifiedAt;
            insert.ExecuteNonQuery();
            count++;
        }, cancellationToken);
        transaction.Commit();
        return (count, skippedLinks);
    }

    private SqliteConnection OpenDatabase()
    {
        var database = ServerStore.CreateConnection(dataDirectory);
        try
        {
            database.Open();
            return database;
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }
}
