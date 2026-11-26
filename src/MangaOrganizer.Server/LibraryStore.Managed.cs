using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record ManagedFile(string Id, string RelativePath, string ContentId, long Size, byte[] Sha256, string SeriesKey,
    string? SeriesTitle, string? Volume, string? Chapter);
internal sealed record FileOperation(Guid Id, string State, Guid SourceRoot, Guid TargetRoot, string SourceBase, string TargetBase,
    string SourcePath, string TargetPath, string FileId, string ContentId, long Size, byte[] Sha256, string SeriesKey,
    string? SeriesTitle, string? Volume, string? Chapter, string? Message, FileOperationCover[] Covers,
    string? QuarantineKeepFile = null, string? QuarantineKeepPath = null, bool IntakeTransfer = false);
internal sealed record FileOperationCover(string SourcePath, string TargetPath, long Size, byte[] Sha256, bool Published = false);

internal sealed partial class LibraryStore
{
    private const string OperationColumns = """
        id, state, source_root, target_root, source_base, target_base, source_path, target_path,
        file_id, content_id, size, sha256, series_key, series_title, volume_hint, chapter_hint, message,
        cover_plan, quarantine_keep_file, quarantine_keep_path, intake_transfer
        """;

    public ManagedFile? ReadManagedFile(Guid root, string fileId)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT f.id, f.relative_path, f.content_id, f.size, c.sha256, f.series_key, f.series_title, f.volume_hint, f.chapter_hint
            FROM library_file f JOIN library_content c ON c.id = f.content_id
            JOIN library_folder folder ON folder.root_id = f.root_id
            WHERE f.root_id = $root AND f.id = $file AND folder.managed = 1;
            """;
        command.Parameters.AddWithValue("$root", root);
        command.Parameters.AddWithValue("$file", fileId);
        using var rows = command.ExecuteReader();
        return rows.Read() ? new(rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetInt64(3), rows.GetFieldValue<byte[]>(4),
            rows.GetString(5), ReadText(rows, 6), ReadText(rows, 7), ReadText(rows, 8)) : null;
    }

    public string? ReadManagedRoot(Guid root)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT path FROM library_folder WHERE root_id = $root AND managed = 1;";
        command.Parameters.AddWithValue("$root", root);
        return command.ExecuteScalar() as string;
    }

    public bool HasOtherSeriesInFolder(Guid root, string path, string series)
    {
        using var database = OpenDatabase();
        return HasOtherSeriesInFolder(database, null, root, path, series);
    }

    private static bool HasOtherSeriesInFolder(SqliteConnection database, SqliteTransaction? transaction, Guid root, string path, string series)
    {
        var directory = path[..(path.LastIndexOf('/') + 1)];
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM library_file WHERE root_id = $root AND series_key <> $series
                AND substr(relative_path, 1, length($directory)) = $directory
                AND instr(substr(relative_path, length($directory) + 1), '/') = 0);
            """;
        command.Parameters.AddWithValue("$root", root);
        command.Parameters.AddWithValue("$directory", directory);
        command.Parameters.AddWithValue("$series", series);
        return (long)command.ExecuteScalar()! != 0;
    }

    public IReadOnlyList<FileOperation> ReadFileOperations()
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT " + OperationColumns + " FROM file_operation ORDER BY created_at DESC LIMIT 50;";
        using var rows = command.ExecuteReader();
        var result = new List<FileOperation>();
        while (rows.Read()) result.Add(ReadOperation(rows));
        return result;
    }

    public FileOperation? ReadFileOperation(Guid id)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT " + OperationColumns + " FROM file_operation WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var rows = command.ExecuteReader();
        return rows.Read() ? ReadOperation(rows) : null;
    }

    public void RecordFileOperation(FileOperation operation)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            INSERT INTO file_operation(id, state, source_root, target_root, source_base, target_base, source_path,
                target_path, file_id, content_id, size, sha256, series_key, series_title, volume_hint, chapter_hint,
                created_at, updated_at, cover_plan, quarantine_keep_file, quarantine_keep_path, intake_transfer)
            VALUES($id, 'planned', $sourceRoot, $targetRoot, $sourceBase, $targetBase, $sourcePath,
                $targetPath, $file, $content, $size, $sha256, $series, $title, $volume, $chapter, $now, $now, $covers, $keepFile, $keepPath, $intake);
            """;
        command.Parameters.AddWithValue("$id", operation.Id);
        command.Parameters.AddWithValue("$sourceRoot", operation.SourceRoot);
        command.Parameters.AddWithValue("$targetRoot", operation.TargetRoot);
        command.Parameters.AddWithValue("$sourceBase", operation.SourceBase);
        command.Parameters.AddWithValue("$targetBase", operation.TargetBase);
        command.Parameters.AddWithValue("$sourcePath", operation.SourcePath);
        command.Parameters.AddWithValue("$targetPath", operation.TargetPath);
        command.Parameters.AddWithValue("$file", operation.FileId);
        command.Parameters.AddWithValue("$content", operation.ContentId);
        command.Parameters.AddWithValue("$size", operation.Size);
        command.Parameters.AddWithValue("$sha256", operation.Sha256);
        AddSeriesParameters(command, operation);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("$covers", JsonSerializer.Serialize(operation.Covers));
        command.Parameters.AddWithValue("$keepFile", (object?)operation.QuarantineKeepFile ?? DBNull.Value);
        command.Parameters.AddWithValue("$keepPath", (object?)operation.QuarantineKeepPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$intake", operation.IntakeTransfer);
        command.ExecuteNonQuery();
    }

    public void RecordCoverPublication(Guid id, FileOperationCover[] covers)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "UPDATE file_operation SET cover_plan = $covers, updated_at = $now WHERE id = $id AND state = 'applying';";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$covers", JsonSerializer.Serialize(covers));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        command.ExecuteNonQuery();
    }

    public bool BeginFileOperation(Guid id, bool restore)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            UPDATE file_operation AS op SET state = $next, updated_at = $now, message = NULL
            WHERE id = $id AND (($restore = 0 AND state = 'planned') OR ($restore = 1 AND state IN ('completed', 'review')))
                AND EXISTS (SELECT 1 FROM library_folder f WHERE f.root_id = op.source_root AND f.path = op.source_base AND f.managed = 1)
                AND EXISTS (SELECT 1 FROM library_folder f WHERE f.root_id = op.target_root AND f.path = op.target_base AND f.managed = 1)
                AND NOT EXISTS (SELECT 1 FROM library_root r WHERE r.id IN (op.source_root, op.target_root) AND r.state IN ('queued', 'scanning'))
                AND ($restore = 1 OR op.intake_transfer = 0 AND (
                    op.source_root = op.target_root OR EXISTS (
                        SELECT 1 FROM library_folder f WHERE f.root_id = op.source_root AND f.kind <> 'import'))
                    OR op.intake_transfer = 1 AND EXISTS (
            """ + " " + IntakeTransferCandidates + """

                        AND f.root_id = op.source_root AND route.target_root = op.target_root
                        AND f.id = op.file_id AND f.content_id = op.content_id AND f.series_key = op.series_key));
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$next", restore ? "restoring" : "applying");
        command.Parameters.AddWithValue("$restore", restore);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        return command.ExecuteNonQuery() == 1;
    }

    public void ReviewFileOperation(Guid id, string message)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "UPDATE file_operation SET state = 'review', message = $message, updated_at = $now WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        command.ExecuteNonQuery();
    }

    private CoverSource? CacheManagedCovers(FileOperation operation, bool restore, DateTimeOffset modified,
        SqliteConnection database, SqliteTransaction transaction)
    {
        var id = restore ? operation.SourceRoot : operation.TargetRoot;
        var root = restore ? operation.SourceBase : operation.TargetBase;
        var path = restore ? operation.SourcePath : operation.TargetPath;
        var cache = new CoverCache(database, transaction);
        var directory = path[..Math.Max(0, path.LastIndexOf('/'))];
        if (ArchivePages.Supports(Path.GetExtension(path)[1..].ToLowerInvariant()) &&
            cache.FindArchive(operation.Sha256, path, operation.Size, modified) is null &&
            CoverArt.FindArchiveCover(cache, root, path, operation.Size, modified, CancellationToken.None) is { } archive)
        {
            cache.RememberArchive(operation.Sha256, archive);
        }

        return HasOtherSeriesInFolder(database, transaction, id, path, operation.SeriesKey)
            ? null : CoverArt.FindSidecars(root, directory, cache).FirstOrDefault();
    }

    private static void RefreshManagedArchiveCover(SqliteConnection database, SqliteTransaction transaction, Guid root, string series)
    {
        using var query = database.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT f.relative_path, f.size, f.modified_at, f.series_title, a.entry, a.image_sha
            FROM library_file f JOIN library_content c ON c.id = f.content_id
            JOIN artwork.archive_cover a ON a.archive_sha = c.sha256
            JOIN artwork.image i ON i.sha256 = a.image_sha
            WHERE f.root_id = $root AND f.series_key = $series AND NOT EXISTS (
                SELECT 1 FROM library_series_cover x WHERE x.root_id = $root AND x.series_key = $series AND x.archive_entry IS NULL)
            ORDER BY f.relative_path;
            """;
        query.Parameters.AddWithValue("$root", root);
        query.Parameters.AddWithValue("$series", series);
        CoverSource? cover = null;
        CoverOrder bestOrder = default;
        using (var rows = query.ExecuteReader())
        {
            while (rows.Read())
            {
                var path = rows.GetString(0);
                var order = CoverOrder.ForFile(path, ReadText(rows, 3));
                if (cover is not null && order.CompareTo(bestOrder) >= 0) continue;
                bestOrder = order;
                cover = new(path, rows.GetInt64(1), rows.GetDateTimeOffset(2), rows.GetString(4), rows.GetFieldValue<byte[]>(5));
            }
        }
        if (cover is not null) UpsertManagedCover(database, transaction, root, series, cover);
    }

    private static void UpsertManagedCover(SqliteConnection database, SqliteTransaction transaction, Guid root, string series, CoverSource cover)
    {
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO library_series_cover(root_id, series_key, relative_path, size, modified_at, archive_entry, sha256)
            VALUES($root, $series, $path, $size, $modified, $entry, $hash)
            ON CONFLICT(root_id, series_key) DO UPDATE SET relative_path = excluded.relative_path, size = excluded.size,
                modified_at = excluded.modified_at, archive_entry = excluded.archive_entry, sha256 = excluded.sha256
            WHERE library_series_cover.archive_entry IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$root", root);
        command.Parameters.AddWithValue("$series", series);
        command.Parameters.AddWithValue("$path", cover.RelativePath);
        command.Parameters.AddWithValue("$size", cover.Size);
        command.Parameters.AddWithValue("$modified", cover.ModifiedAt);
        command.Parameters.AddWithValue("$entry", (object?)cover.ArchiveEntry ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", cover.Sha256);
        command.ExecuteNonQuery();
    }

    public void FinishFileOperation(FileOperation operation, bool restore, DateTimeOffset modified, bool retainTarget)
    {
        var root = restore ? operation.SourceRoot : operation.TargetRoot;
        var path = restore ? operation.SourcePath : operation.TargetPath;
        using var database = OpenCoverDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        var publishToCatalog = restore || operation.QuarantineKeepFile is null;
        var sidecar = publishToCatalog ? CacheManagedCovers(operation, restore, modified, database, transaction) : null;
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", operation.Id);
        command.Parameters.AddWithValue("$root", root);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$file", operation.FileId);
        command.Parameters.AddWithValue("$content", operation.ContentId);
        command.Parameters.AddWithValue("$sourceRoot", operation.SourceRoot);
        command.Parameters.AddWithValue("$targetRoot", operation.TargetRoot);
        command.Parameters.AddWithValue("$modified", modified);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("$state", restore ? "restored" : "completed");
        command.Parameters.AddWithValue("$message", retainTarget ? "The destination was retained for inspection." : DBNull.Value);
        command.Parameters.AddWithValue("$targetPath", operation.TargetPath);
        command.Parameters.AddWithValue("$sourcePath", operation.SourcePath);
        if (publishToCatalog)
        {
            command.CommandText = """
                DELETE FROM library_file WHERE root_id = $root AND relative_path = $path AND id <> $file;
                """;
            command.ExecuteNonQuery();
            command.CommandText = """
                UPDATE library_file SET root_id = $root, relative_path = $path, modified_at = $modified
                WHERE id = $file AND content_id = $content;
                """;
            if (command.ExecuteNonQuery() == 0)
            {
                AddSeriesParameters(command, operation);
                command.Parameters.AddWithValue("$extension", Path.GetExtension(path)[1..].ToLowerInvariant());
                command.Parameters.AddWithValue("$size", operation.Size);
                command.CommandText = """
                    INSERT INTO library_file(root_id, relative_path, extension, size, modified_at, content_id, series_key, series_title, volume_hint, chapter_hint)
                    VALUES($root, $path, $extension, $size, $modified, $content, $series, $title, $volume, $chapter);
                    """;
                command.ExecuteNonQuery();
            }
        }
        if (restore ? !retainTarget : true)
        {
            command.CommandText = restore
                ? "DELETE FROM library_file WHERE root_id = $targetRoot AND relative_path = $targetPath AND content_id = $content;"
                : "DELETE FROM library_file WHERE root_id = $sourceRoot AND relative_path = $sourcePath AND content_id = $content;";
            command.ExecuteNonQuery();
        }
        command.CommandText = """
            UPDATE library_root SET file_count = (SELECT COUNT(*) FROM library_file WHERE root_id = library_root.id)
                WHERE id IN ($sourceRoot, $targetRoot);
            DELETE FROM library_series_cover
                WHERE root_id IN ($sourceRoot, $targetRoot) AND archive_entry IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM library_file f WHERE f.root_id = library_series_cover.root_id
                    AND f.relative_path = library_series_cover.relative_path);
            UPDATE file_operation SET state = $state, message = $message, updated_at = $now WHERE id = $id;
            """;
        command.ExecuteNonQuery();
        if (sidecar is not null) UpsertManagedCover(database, transaction, root, operation.SeriesKey, sidecar);
        RefreshManagedArchiveCover(database, transaction, operation.SourceRoot, operation.SeriesKey);
        if (operation.SourceRoot != operation.TargetRoot)
        {
            RefreshManagedArchiveCover(database, transaction, operation.TargetRoot, operation.SeriesKey);
        }
        transaction.Commit();
    }

    private static FileOperation ReadOperation(SqliteDataReader rows) => new(rows.GetGuid(0), rows.GetString(1), rows.GetGuid(2),
        rows.GetGuid(3), rows.GetString(4), rows.GetString(5), rows.GetString(6), rows.GetString(7), rows.GetString(8), rows.GetString(9),
        rows.GetInt64(10), rows.GetFieldValue<byte[]>(11), rows.GetString(12), ReadText(rows, 13), ReadText(rows, 14),
        ReadText(rows, 15), ReadText(rows, 16), JsonSerializer.Deserialize<FileOperationCover[]>(rows.GetString(17)) ?? [],
        ReadText(rows, 18), ReadText(rows, 19), rows.GetBoolean(20));

    private static void AddSeriesParameters(SqliteCommand command, FileOperation operation)
    {
        command.Parameters.AddWithValue("$series", operation.SeriesKey);
        command.Parameters.AddWithValue("$title", (object?)operation.SeriesTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$volume", (object?)operation.Volume ?? DBNull.Value);
        command.Parameters.AddWithValue("$chapter", (object?)operation.Chapter ?? DBNull.Value);
    }
}
