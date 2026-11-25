using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record ServerIdentity(Guid InstanceId, Guid OwnerId, string PasswordHash);

internal static class ServerStore
{
    public static SqliteConnection CreateConnection(string dataDirectory) => new(new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(dataDirectory, "organizer.sqlite"),
        Pooling = false,
    }.ToString());

    public static ServerIdentity Open(string dataDirectory, string? bootstrapPassword)
    {
        using var database = CreateConnection(dataDirectory);
        database.Open();

        // Serialize initialization so only one process can create the owner and server identity.
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS server_identity (
                singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
                instance_id TEXT NOT NULL,
                owner_id TEXT NOT NULL,
                password_hash TEXT NOT NULL
            );
            SELECT instance_id, owner_id, password_hash FROM server_identity WHERE singleton = 1;
            """;

        ServerIdentity? identity = null;
        using (var rows = command.ExecuteReader())
        {
            if (rows.Read())
            {
                identity = new ServerIdentity(rows.GetGuid(0), rows.GetGuid(1), rows.GetString(2));
            }
        }

        if (identity is null)
        {
            if (bootstrapPassword is not { Length: >= 12 and <= 256 } || string.IsNullOrWhiteSpace(bootstrapPassword))
            {
                throw new InvalidOperationException("Set MANGA_ORGANIZER_BOOTSTRAP_PASSWORD to a password of 12–256 characters before first startup.");
            }

            var ownerId = Guid.NewGuid();
            var passwordHash = new PasswordHasher<string>().HashPassword(ownerId.ToString("D"), bootstrapPassword);
            identity = new ServerIdentity(Guid.NewGuid(), ownerId, passwordHash);
            command.CommandText = """
                INSERT INTO server_identity (singleton, instance_id, owner_id, password_hash)
                VALUES (1, $instance, $owner, $hash);
                """;
            command.Parameters.AddWithValue("$instance", identity.InstanceId);
            command.Parameters.AddWithValue("$owner", identity.OwnerId);
            command.Parameters.AddWithValue("$hash", identity.PasswordHash);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return identity;
    }
}
