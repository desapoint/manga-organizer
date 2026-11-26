using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record LibraryFolderChange(string? Path, string? Name, string? Kind);
internal sealed record LibraryFolder(Guid Id, string Path, string? Name, string Kind);

internal sealed class LibraryFolderBusyException() : Exception("Wait for the folder scan to finish before changing this folder.");

internal sealed partial class LibraryStore
{
    private const string CreateFolderTable = """
        CREATE TABLE IF NOT EXISTS library_folder (
            root_id TEXT NOT NULL PRIMARY KEY REFERENCES library_root(id),
            path TEXT NOT NULL,
            name TEXT,
            kind TEXT NOT NULL CHECK (kind IN ('library', 'import', 'both'))
        );
        """;

    private static void InitializeFolders(SqliteCommand command)
    {
        command.CommandText = "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'library_folder';";
        var schema = command.ExecuteScalar() as string;
        if (schema is null)
        {
            command.CommandText = CreateFolderTable;
            command.ExecuteNonQuery();
        }
        else if (!schema.Contains("'both'", StringComparison.Ordinal))
        {
            command.CommandText = "ALTER TABLE library_folder RENAME TO legacy_library_folder;" + CreateFolderTable + """
                INSERT INTO library_folder(root_id, path, name, kind)
                    SELECT root_id, path, name, kind FROM legacy_library_folder ORDER BY rowid;
                DROP TABLE legacy_library_folder;
                """;
            command.ExecuteNonQuery();
        }
    }

    public LibraryFolder? SaveFolder(Guid? id, LibraryFolderChange change)
    {
        if (change.Kind is not ("library" or "import" or "both"))
        {
            throw new ArgumentException("Choose library, import intake, or both.");
        }

        var name = string.IsNullOrWhiteSpace(change.Name) ? null : change.Name.Trim();
        var path = LibraryScanner.NormalizeRoot(change.Path, dataDirectory, []);
        LibraryScanner.RequireRoot(path);
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        if (id is { } currentId && !CanChangeFolder(command, currentId))
        {
            return null;
        }

        command.CommandText = "SELECT root_id, path FROM library_folder;";
        var otherPaths = new List<string>();
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                if (rows.GetGuid(0) != id)
                {
                    otherPaths.Add(rows.GetString(1));
                }
            }
        }

        LibraryScanner.NormalizeRoot(path, dataDirectory, otherPaths);
        var folder = SaveFolder(command, id, path, name, change.Kind);
        transaction.Commit();
        return folder;
    }

    public bool RemoveFolder(Guid id)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        if (!CanChangeFolder(command, id))
        {
            return false;
        }

        command.CommandText = "DELETE FROM library_folder WHERE root_id = $id;";
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    private static bool CanChangeFolder(SqliteCommand command, Guid id)
    {
        command.Parameters.Clear();
        command.CommandText = """
            SELECT r.state FROM library_folder f JOIN library_root r ON r.id = f.root_id
            WHERE f.root_id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        var state = command.ExecuteScalar() as string;
        if (state is "queued" or "scanning")
        {
            throw new LibraryFolderBusyException();
        }

        return state is not null;
    }

    private static LibraryFolder SaveFolder(SqliteCommand command, Guid? currentId, string path, string? name, string kind)
    {
        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO library_root(id, path_key) VALUES ($id, $pathKey)
            ON CONFLICT(path_key) DO UPDATE SET path_key = excluded.path_key RETURNING id;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid());
        command.Parameters.AddWithValue("$pathKey", OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path);
        var rootId = Guid.Parse((string)command.ExecuteScalar()!);

        command.Parameters.Clear();
        command.CommandText = currentId is null
            ? "INSERT INTO library_folder(root_id, path, name, kind) VALUES ($id, $path, $name, $kind);"
            : "UPDATE library_folder SET root_id = $id, path = $path, name = $name, kind = $kind WHERE root_id = $currentId;";
        command.Parameters.AddWithValue("$id", rootId);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$name", (object?)name ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", kind);
        if (currentId is not null)
        {
            command.Parameters.AddWithValue("$currentId", currentId.Value);
        }
        command.ExecuteNonQuery();
        return new LibraryFolder(rootId, path, name, kind);
    }
}
