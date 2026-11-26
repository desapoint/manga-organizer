using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record LibraryRoot(Guid Id, string Path);
internal sealed record LibraryRootStatus(Guid Id, string Path, string State, long FileCount,
    DateTimeOffset? LastAttemptAt, DateTimeOffset? LastScannedAt, long SkippedLinks, string? Name, string Kind);
internal sealed record CatalogEntry(string Id, string RelativePath, string Extension, long Size, DateTimeOffset ModifiedAt,
    string? ContentId, string? Sha256, long? IndexedLocations);
internal sealed record CatalogPage(long Total, IReadOnlyList<CatalogEntry> Items);

internal sealed partial class LibraryStore
{
    private readonly string dataDirectory;

    public LibraryStore(string dataDirectory, Func<IReadOnlyList<string>> initialPaths)
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
            CREATE TABLE IF NOT EXISTS library_content (
                id TEXT PRIMARY KEY NOT NULL DEFAULT (lower(hex(randomblob(16)))),
                sha256 BLOB NOT NULL UNIQUE CHECK (length(sha256) = 32)
            );
            CREATE TABLE IF NOT EXISTS library_file (
                id TEXT PRIMARY KEY DEFAULT (lower(hex(randomblob(16)))),
                root_id TEXT NOT NULL REFERENCES library_root(id),
                relative_path TEXT NOT NULL,
                extension TEXT NOT NULL,
                size INTEGER NOT NULL,
                modified_at TEXT NOT NULL,
                content_id TEXT REFERENCES library_content(id),
                UNIQUE(root_id, relative_path)
            );
            CREATE TABLE IF NOT EXISTS library_folder (
                root_id TEXT NOT NULL PRIMARY KEY REFERENCES library_root(id),
                path TEXT NOT NULL,
                name TEXT,
                kind TEXT NOT NULL CHECK (kind IN ('library', 'import'))
            );
            CREATE TABLE IF NOT EXISTS library_settings (
                singleton INTEGER PRIMARY KEY CHECK (singleton = 1)
            );
            UPDATE library_root SET state = 'interrupted' WHERE state IN ('queued', 'scanning');
            """;
        command.ExecuteNonQuery();

        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('library_file') WHERE name = 'content_id';";
        if ((long)command.ExecuteScalar()! == 0)
        {
            command.CommandText = "ALTER TABLE library_file ADD COLUMN content_id TEXT REFERENCES library_content(id);";
            command.ExecuteNonQuery();
        }
        command.CommandText = "CREATE INDEX IF NOT EXISTS library_file_content ON library_file(content_id, root_id);";
        command.ExecuteNonQuery();

        command.CommandText = "INSERT OR IGNORE INTO library_settings(singleton) VALUES (1);";
        if (command.ExecuteNonQuery() == 1)
        {
            foreach (var path in initialPaths())
            {
                SaveFolder(command, null, path, null, "library");
            }
        }
        transaction.Commit();
    }

    public bool TryGetRoot(Guid id, out LibraryRoot? root)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT path FROM library_folder WHERE root_id = $id;";
        command.Parameters.AddWithValue("$id", id);
        root = command.ExecuteScalar() is string path ? new LibraryRoot(id, path) : null;
        return root is not null;
    }

    public IReadOnlyList<LibraryRootStatus> ReadRoots()
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT r.id, f.path, r.state, r.file_count, r.last_attempt_at,
                r.last_scanned_at, r.skipped_links, f.name, f.kind
            FROM library_folder f JOIN library_root r ON r.id = f.root_id
            ORDER BY f.rowid;
            """;
        var result = new List<LibraryRootStatus>();
        using var row = command.ExecuteReader();
        while (row.Read())
        {
            result.Add(new(row.GetGuid(0), row.GetString(1), row.GetString(2), row.GetInt64(3),
                row.IsDBNull(4) ? null : row.GetDateTimeOffset(4),
                row.IsDBNull(5) ? null : row.GetDateTimeOffset(5), row.GetInt64(6),
                row.IsDBNull(7) ? null : row.GetString(7), row.GetString(8)));
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
            SELECT f.id, f.relative_path, f.extension, f.size, f.modified_at, c.id, lower(hex(c.sha256)),
                CASE WHEN c.id IS NOT NULL THEN (
                    SELECT COUNT(*) FROM library_file location
                    JOIN library_folder folder ON folder.root_id = location.root_id
                    WHERE location.content_id = c.id
                ) END
            FROM library_file f LEFT JOIN library_content c ON c.id = f.content_id
            WHERE f.root_id = $id ORDER BY f.relative_path LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<CatalogEntry>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var verified = !rows.IsDBNull(5);
            items.Add(new(rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetInt64(3), rows.GetDateTimeOffset(4),
                verified ? rows.GetString(5) : null, verified ? rows.GetString(6) : null, verified ? rows.GetInt64(7) : null));
        }
        return new(total, items);
    }

    public bool TryQueue(Guid id)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            UPDATE library_root SET state = 'queued', last_attempt_at = $now
            WHERE id = $id AND state NOT IN ('queued', 'scanning')
                AND EXISTS (SELECT 1 FROM library_folder WHERE root_id = $id);
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
                modified_at TEXT NOT NULL,
                sha256 BLOB
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
                INSERT INTO library_content(sha256)
                    SELECT DISTINCT sha256 FROM scan_file WHERE sha256 IS NOT NULL
                    ON CONFLICT(sha256) DO NOTHING;
                INSERT INTO library_file(root_id, relative_path, extension, size, modified_at, content_id)
                    SELECT $id, s.relative_path, s.extension, s.size, s.modified_at, c.id
                    FROM scan_file s LEFT JOIN library_content c ON c.sha256 = s.sha256 WHERE 1
                    ON CONFLICT(root_id, relative_path) DO UPDATE SET
                        extension = excluded.extension, size = excluded.size, modified_at = excluded.modified_at,
                        content_id = excluded.content_id;
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
        insert.CommandText = "INSERT INTO scan_file VALUES ($path, $extension, $size, $modified, $sha256);";
        var relativePath = insert.Parameters.Add("$path", SqliteType.Text);
        var extension = insert.Parameters.Add("$extension", SqliteType.Text);
        var size = insert.Parameters.Add("$size", SqliteType.Integer);
        var modified = insert.Parameters.Add("$modified", SqliteType.Text);
        var sha256 = insert.Parameters.Add("$sha256", SqliteType.Blob);
        insert.Prepare();

        long count = 0;
        var skippedLinks = LibraryScanner.Collect(path, file =>
        {
            relativePath.Value = file.RelativePath;
            extension.Value = file.Extension;
            size.Value = file.Size;
            modified.Value = file.ModifiedAt;
            sha256.Value = (object?)file.Sha256 ?? DBNull.Value;
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
