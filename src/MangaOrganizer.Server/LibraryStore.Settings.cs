using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record LibraryFolderChange(string? Path, string? Name, string? Kind, bool? Managed);
internal sealed record LibraryFolder(Guid Id, string Path, string? Name, string Kind, bool Managed);

internal sealed class LibraryFolderBusyException() : Exception("Wait for the scan or file operation to finish before changing this folder.");

internal sealed partial class LibraryStore
{
    internal static void UpgradeFolderRoles(SqliteCommand command, string createSql)
    {
        command.CommandText = "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'library_folder';";
        var schema = command.ExecuteScalar() as string;
        if (schema is not null && !schema.Contains("'both'", StringComparison.Ordinal))
        {
            command.CommandText = "ALTER TABLE library_folder RENAME TO legacy_library_folder;" + createSql + """
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
        var folder = SaveFolder(command, id, path, name, change.Kind, change.Managed);
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
            SELECT CASE WHEN EXISTS (SELECT 1 FROM file_operation op WHERE op.state IN ('applying', 'restoring')
                AND ($id IN (op.source_root, op.target_root) OR op.state = 'applying' AND $id = op.quarantine_keep_root))
                THEN 'applying' ELSE r.state END
            FROM library_folder f JOIN library_root r ON r.id = f.root_id WHERE f.root_id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        var state = command.ExecuteScalar() as string;
        if (state is "queued" or "scanning" or "applying")
        {
            throw new LibraryFolderBusyException();
        }

        return state is not null;
    }

    private static LibraryFolder SaveFolder(SqliteCommand command, Guid? currentId, string path, string? name, string kind, bool? managed = null)
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
            ? "INSERT INTO library_folder(root_id, path, name, kind, managed) VALUES ($id, $path, $name, $kind, COALESCE($managed, 0)) RETURNING managed;"
            : "UPDATE library_folder SET root_id = $id, path = $path, name = $name, kind = $kind, managed = COALESCE($managed, managed) WHERE root_id = $currentId RETURNING managed;";
        command.Parameters.AddWithValue("$id", rootId);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$name", (object?)name ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$managed", (object?)managed ?? DBNull.Value);
        if (currentId is not null)
        {
            command.Parameters.AddWithValue("$currentId", currentId.Value);
        }
        var isManaged = (long)command.ExecuteScalar()! == 1;
        return new LibraryFolder(rootId, path, name, kind, isManaged);
    }
}
