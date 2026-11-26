using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace MangaOrganizer.Server;

internal sealed class CoverCache(SqliteConnection database, SqliteTransaction? transaction = null)
{
    public const string MediaType = "image/webp";
    private const int MaxEdge = 2048;

    public static void Attach(SqliteConnection database, string dataDirectory)
    {
        using var command = database.CreateCommand();
        command.CommandText = "ATTACH DATABASE $path AS artwork;";
        command.Parameters.AddWithValue("$path", Path.Combine(dataDirectory, "cover-cache.sqlite"));
        command.ExecuteNonQuery();
    }

    public static void Initialize(SqliteConnection database)
    {
        using var command = database.CreateCommand();
        command.CommandText = """
            PRAGMA artwork.auto_vacuum = INCREMENTAL;
            PRAGMA artwork.journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS artwork.image (
                sha256 BLOB PRIMARY KEY CHECK (length(sha256) = 32),
                data BLOB NOT NULL
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS artwork.archive_cover (
                archive_sha BLOB PRIMARY KEY CHECK (length(archive_sha) = 32),
                image_sha BLOB NOT NULL REFERENCES image(sha256),
                entry TEXT NOT NULL
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS artwork.folder_cover (
                root_id TEXT NOT NULL,
                series_key TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                image_sha BLOB NOT NULL REFERENCES image(sha256),
                PRIMARY KEY(root_id, relative_path)
            ) WITHOUT ROWID;
            """;
        command.ExecuteNonQuery();
    }

    public byte[]? Store(RasterImage image)
    {
        var digest = SHA256.HashData(image.Bytes);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM artwork.image WHERE sha256 = $digest;";
        command.Parameters.AddWithValue("$digest", digest);
        if (command.ExecuteScalar() is not null) return digest;

        using var bitmap = SKBitmap.Decode(image.Bytes);
        if (bitmap is null) return null;
        var scale = Math.Min(1d, (double)MaxEdge / Math.Max(bitmap.Width, bitmap.Height));
        var size = new SKImageInfo(Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
            Math.Max(1, (int)Math.Round(bitmap.Height * scale)), bitmap.ColorType, bitmap.AlphaType, bitmap.ColorSpace);
        using var resized = scale < 1 ? bitmap.Resize(size, new SKSamplingOptions(SKCubicResampler.Mitchell)) : null;
        if (scale < 1 && resized is null) return null;
        using var encoded = (resized ?? bitmap).Encode(SKEncodedImageFormat.Webp, 88);
        if (encoded is null) return null;

        command.CommandText = "INSERT OR IGNORE INTO artwork.image(sha256, data) VALUES ($digest, $data);";
        command.Parameters.AddWithValue("$data", encoded.ToArray());
        command.ExecuteNonQuery();
        return digest;
    }

    public byte[]? Read(byte[] digest)
    {
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT data FROM artwork.image WHERE sha256 = $digest;";
        command.Parameters.AddWithValue("$digest", digest);
        return command.ExecuteScalar() as byte[];
    }

    public CoverSource? FindArchive(byte[] archiveDigest, string path, long size, DateTimeOffset modified)
    {
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT c.entry, c.image_sha FROM artwork.archive_cover c JOIN artwork.image i ON i.sha256 = c.image_sha
            WHERE c.archive_sha = $archive;
            """;
        command.Parameters.AddWithValue("$archive", archiveDigest);
        using var rows = command.ExecuteReader();
        return rows.Read() ? new(path, size, modified, rows.GetString(0), rows.GetFieldValue<byte[]>(1)) : null;
    }

    public void RememberArchive(byte[] archiveDigest, CoverSource cover)
    {
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO artwork.archive_cover(archive_sha, image_sha, entry) VALUES ($archive, $image, $entry)
            ON CONFLICT(archive_sha) DO UPDATE SET image_sha = excluded.image_sha, entry = excluded.entry;
            """;
        command.Parameters.AddWithValue("$archive", archiveDigest);
        command.Parameters.AddWithValue("$image", cover.Sha256);
        command.Parameters.AddWithValue("$entry", cover.ArchiveEntry!);
        command.ExecuteNonQuery();
    }
}
