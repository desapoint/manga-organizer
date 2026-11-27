using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal static class UpgradeBackup
{
    private const string DatabaseName = "organizer.sqlite";

    private sealed record BackupFile(string Path, long Bytes, string Sha256);

    public static string Create(string dataDirectory, SqliteCommand sourceCommand, IReadOnlyList<string> tables, int previousSchema, int targetSchema,
        bool maintenance = false)
    {
        if (tables.Contains("file_operation"))
        {
            sourceCommand.CommandText = maintenance
                ? "SELECT 1 FROM file_operation WHERE state IN ('applying', 'restoring') LIMIT 1;"
                : "SELECT 1 FROM file_operation WHERE state IN ('applying', 'restoring', 'review') LIMIT 1;";
            if (sourceCommand.ExecuteScalar() is not null)
                throw new InvalidOperationException("Resolve interrupted file operations with the previous version before upgrading.");
        }

        var backupDirectory = Path.Combine(dataDirectory, maintenance ? "maintenance-backups" : "upgrade-backups");
        CreatePrivateDirectory(backupDirectory);
        var timestamp = DateTimeOffset.UtcNow;
        var name = $"{timestamp:yyyyMMddTHHmmssfffZ}-schema-{previousSchema}-{Guid.NewGuid():N}";
        var staging = Path.Combine(backupDirectory, "." + name + ".part");
        var published = Path.Combine(backupDirectory, name);
        CreatePrivateDirectory(staging);
        var snapshotPath = Path.Combine(staging, DatabaseName);
        var originalPath = Path.Combine(dataDirectory, DatabaseName);
        RequireOrdinaryPath(originalPath);

        // The migration connection holds a reserved write lock. Read a separate connection so SQLite
        // can copy the committed snapshot, including changes still in WAL, without copying sidecars.
        using (var original = Open(originalPath, SqliteOpenMode.ReadOnly))
        using (var snapshot = Open(snapshotPath, SqliteOpenMode.ReadWriteCreate))
        {
            original.BackupDatabase(snapshot);
        }

        var counts = VerifyDatabase(snapshotPath, sourceCommand, tables);
        using (var snapshot = new FileStream(snapshotPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            snapshot.Flush(flushToDisk: true);

        var files = new List<BackupFile> { GetFile(staging, DatabaseName) };
        CopyKeys(dataDirectory, staging, files);
        var manifest = new
        {
            Format = 1,
            CreatedAtUtc = timestamp,
            ApplicationVersion = typeof(UpgradeBackup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            SourceSchema = previousSchema,
            TargetSchema = targetSchema,
            RestoreWith = maintenance ? "Use the Manga Organizer version that created this maintenance snapshot." :
                "Use the Manga Organizer version that was running before this upgrade.",
            TableCounts = counts,
            Files = files,
            RecoveryFiles = "Media and file-recovery bytes remain in their original locations.",
        };
        using (var output = new FileStream(Path.Combine(staging, "manifest.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, manifest, new JsonSerializerOptions { WriteIndented = true });
            output.Flush(flushToDisk: true);
        }
        foreach (var file in files)
        {
            if (file != GetFile(staging, file.Path)) throw new IOException("A staged upgrade-backup file failed its checksum.");
        }
        Publish(dataDirectory, backupDirectory, staging, published);
        return published;
    }

    internal static void RequireOrdinaryPath(string path)
    {
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("A server data or backup path is a symbolic link or reparse point.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ToString());
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

    private static Dictionary<string, long> VerifyDatabase(string path, SqliteCommand original, IReadOnlyList<string> tables)
    {
        using var database = Open(path, SqliteOpenMode.ReadOnly);
        using var check = database.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        if (check.ExecuteScalar() as string != "ok") throw new IOException("The upgrade backup failed SQLite integrity verification.");
        check.CommandText = "PRAGMA foreign_key_check;";
        if (check.ExecuteScalar() is not null) throw new IOException("The upgrade backup has inconsistent SQLite references.");

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
            check.CommandText = original.CommandText = "SELECT COUNT(*) FROM " + quoted;
            var expected = (long)original.ExecuteScalar()!;
            if ((long)check.ExecuteScalar()! != expected) throw new IOException("The upgrade backup table counts differ from the source.");
            counts.Add(table, expected);
        }
        if (counts.ContainsKey("schema_migration"))
            VerifyRows(original, check, "SELECT version, name, applied_at FROM schema_migration ORDER BY version;");
        if (counts.ContainsKey("server_identity"))
            VerifyRows(original, check, "SELECT instance_id, owner_id, password_hash FROM server_identity ORDER BY singleton;");
        return counts;
    }

    private static void VerifyRows(SqliteCommand original, SqliteCommand backup, string sql)
    {
        original.CommandText = backup.CommandText = sql;
        using var expected = original.ExecuteReader();
        using var rows = backup.ExecuteReader();
        while (expected.Read())
        {
            if (!rows.Read()) throw new IOException("The upgrade backup record inventory differs from the source.");
            for (var column = 0; column < expected.FieldCount; column++)
            {
                if (!Equals(expected.GetValue(column), rows.GetValue(column)))
                    throw new IOException("The upgrade backup record inventory differs from the source.");
            }
        }
        if (rows.Read()) throw new IOException("The upgrade backup record inventory differs from the source.");
    }

    private static void CopyKeys(string original, string staging, List<BackupFile> files)
    {
        var keys = Path.Combine(original, "keys");
        RequireOrdinaryPath(keys);
        if (!Directory.Exists(keys)) return;

        CreatePrivateDirectory(Path.Combine(staging, "keys"));
        foreach (var path in Directory.EnumerateFiles(keys, "*.xml").Order(StringComparer.Ordinal))
        {
            RequireOrdinaryPath(path);
            var relative = "keys/" + Path.GetFileName(path);
            using var input = File.OpenRead(path);
            using var output = new FileStream(Path.Combine(staging, relative), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
            output.Close();
            var copied = GetFile(staging, relative);
            if (copied != GetFile(original, relative)) throw new IOException("A server key changed while the upgrade backup was copied.");
            files.Add(copied);
        }
    }

    private static void Publish(string dataDirectory, string backupDirectory, string staging, string published)
    {
        if (OperatingSystem.IsWindows())
        {
            const int writeThrough = 0x8;
            if (!MoveFileEx(staging, published, writeThrough)) ThrowFileError("publish the verified upgrade backup");
            return;
        }

        var keys = Path.Combine(staging, "keys");
        if (Directory.Exists(keys)) FlushDirectory(keys);
        FlushDirectory(staging);
        Directory.Move(staging, published);
        FlushDirectory(backupDirectory);
        FlushDirectory(dataDirectory);
    }

    private static void FlushDirectory(string path)
    {
        const int readOnly = 0;
        var handle = OpenDirectory(path, readOnly);
        if (handle < 0) ThrowFileError("open the upgrade-backup directory for synchronization");
        try
        {
            if (SyncDirectory(handle) != 0) ThrowFileError("synchronize the upgrade-backup directory");
        }
        finally
        {
            CloseDirectory(handle);
        }
    }

    private static void ThrowFileError(string action) =>
        throw new IOException($"Could not {action}.", new Win32Exception(Marshal.GetLastPInvokeError()));

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingPath, string newPath, int flags);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenDirectory([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int SyncDirectory(int handle);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int CloseDirectory(int handle);

    private static void CreatePrivateDirectory(string path)
    {
        RequireOrdinaryPath(path);
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
        {
            const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(path, ownerOnly);
            if ((File.GetUnixFileMode(path) & ~ownerOnly) != 0) throw new IOException("The upgrade-backup directory must be private to the server account.");
        }
    }

    private static BackupFile GetFile(string directory, string relative)
    {
        using var input = File.OpenRead(Path.Combine(directory, relative));
        return new(relative, input.Length, Convert.ToHexStringLower(SHA256.HashData(input)));
    }
}
