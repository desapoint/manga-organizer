using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal static class DatabaseMigrations
{
    private sealed record Migration(string Name, Action<SqliteConnection, SqliteCommand, string>? Upgrade = null);

    private static readonly Migration[] Migrations =
    [
        new("0001-foundation"),
        new("0002-content-identity", (_, command, _) => AddContentIdentity(command)),
        new("0003-folder-series", (database, command, sql) =>
        {
            LibraryStore.UpgradeFolderRoles(command, sql);
            LibraryStore.InitializeSeries(database, command);
        }),
        new("0004-cover-sources"),
        new("0005-series-metadata", (database, command, _) => LibraryStore.BackfillBookNumbers(database, command)),
        new("0006-reading-progress"),
        new("0007-managed-files"),
        new("0008-operation-artwork"),
        new("0009-anilist-details"),
        new("0010-series-collections"),
        new("0011-inferred-volume-chapters"),
        new("0012-chapter-editions"),
        new("0013-duplicate-quarantine"),
        new("0014-preferred-chapter-release"),
        new("0015-metadata-suggestions"),
        new("0016-intake-destinations"),
        new("0017-release-tags"),
        new("0018-maintenance-resets"),
    ];

    public static int Apply(string dataDirectory)
    {
        using var database = ServerStore.CreateConnection(dataDirectory);
        UpgradeBackup.RequireOrdinaryPath(database.DataSource);
        database.Open();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT name FROM sqlite_schema WHERE type = 'table' AND substr(name, 1, 7) <> 'sqlite_' ORDER BY name;
            """;
        var tables = new List<string>();
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read()) tables.Add(rows.GetString(0));
        }
        var hasHistory = tables.Contains("schema_migration", StringComparer.OrdinalIgnoreCase);
        var applied = hasHistory ? ReadHistory(command) : 0;
        if (applied == Migrations.Length) return applied;
        if (tables.Count > 0)
        {
            var snapshot = UpgradeBackup.Create(dataDirectory, command, tables, applied, Migrations.Length);
            Console.WriteLine($"Verified pre-upgrade backup saved in {snapshot}");
        }
        if (!hasHistory)
        {
            command.CommandText = """
                CREATE TABLE schema_migration (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL UNIQUE,
                    applied_at TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        for (var index = applied; index < Migrations.Length; index++)
        {
            var migration = Migrations[index];
            var sql = ReadSql(migration.Name);
            command.Parameters.Clear();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            migration.Upgrade?.Invoke(database, command, sql);

            command.Parameters.Clear();
            command.CommandText = "INSERT INTO schema_migration(version, name, applied_at) VALUES ($version, $name, $appliedAt);";
            command.Parameters.AddWithValue("$version", index + 1);
            command.Parameters.AddWithValue("$name", migration.Name);
            command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return Migrations.Length;
    }

    private static int ReadHistory(SqliteCommand command)
    {
        command.CommandText = "SELECT version, name FROM schema_migration ORDER BY version;";
        var applied = 0;
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            if (applied >= Migrations.Length || rows.GetInt64(0) != applied + 1 || rows.GetString(1) != Migrations[applied].Name)
            {
                throw new InvalidOperationException("The database migration history is incompatible with this version of Manga Organizer.");
            }
            applied++;
        }
        return applied;
    }

    private static string ReadSql(string name)
    {
        var resource = $"{typeof(DatabaseMigrations).Namespace}.Migrations.{name}.sql";
        using var stream = typeof(DatabaseMigrations).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded database migration {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AddContentIdentity(SqliteCommand command)
    {
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('library_file') WHERE name = 'content_id';";
        if ((long)command.ExecuteScalar()! == 0)
        {
            command.CommandText = "ALTER TABLE library_file ADD COLUMN content_id TEXT REFERENCES library_content(id);";
            command.ExecuteNonQuery();
        }

        command.CommandText = "CREATE INDEX IF NOT EXISTS library_file_content ON library_file(content_id, root_id);";
        command.ExecuteNonQuery();
    }
}
