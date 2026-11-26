using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record ServerIdentity(Guid InstanceId, Guid OwnerId, string PasswordHash, string? LibraryPath);

internal static class ServerStore
{
    public static SqliteConnection CreateConnection(string dataDirectory) => new(new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(dataDirectory, "organizer.sqlite"),
        Pooling = false,
    }.ToString());

    public static bool IsValidPassword([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? password) =>
        password is { Length: >= 12 and <= 256 } && !string.IsNullOrWhiteSpace(password);

    public static (ServerIdentity? Identity, bool Created) Open(string dataDirectory, string? bootstrapPassword, string? libraryPath = null)
    {
        using var database = CreateConnection(dataDirectory);
        database.Open();

        // Serialize initialization so only one process can create the owner and server identity.
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT i.instance_id, i.owner_id, i.password_hash, s.library_path
            FROM server_identity i LEFT JOIN server_setup s ON i.singleton = s.singleton
            WHERE i.singleton = 1;
            """;

        ServerIdentity? identity = null;
        using (var rows = command.ExecuteReader())
        {
            if (rows.Read())
            {
                identity = new ServerIdentity(rows.GetGuid(0), rows.GetGuid(1), rows.GetString(2),
                    rows.IsDBNull(3) ? null : rows.GetString(3));
            }
        }

        var created = identity is null;
        if (identity is null)
        {
            if (!IsValidPassword(bootstrapPassword))
            {
                return (null, false);
            }

            var ownerId = Guid.NewGuid();
            var passwordHash = new PasswordHasher<string>().HashPassword(ownerId.ToString("D"), bootstrapPassword);
            identity = new ServerIdentity(Guid.NewGuid(), ownerId, passwordHash, libraryPath);
            command.CommandText = """
                INSERT INTO server_identity (singleton, instance_id, owner_id, password_hash)
                VALUES (1, $instance, $owner, $hash);
                """;
            command.Parameters.AddWithValue("$instance", identity.InstanceId);
            command.Parameters.AddWithValue("$owner", identity.OwnerId);
            command.Parameters.AddWithValue("$hash", identity.PasswordHash);
            command.ExecuteNonQuery();
            if (libraryPath is not null)
            {
                command.Parameters.Clear();
                command.CommandText = "INSERT INTO server_setup (singleton, library_path) VALUES (1, $path);";
                command.Parameters.AddWithValue("$path", libraryPath);
                command.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return (identity, created);
    }
}
