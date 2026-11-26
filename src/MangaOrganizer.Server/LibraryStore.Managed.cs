using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record ManagedFile(string Id, string RelativePath, string ContentId, long Size, byte[] Sha256, string SeriesKey,
    string? SeriesTitle, string? Volume, string? Chapter);
internal sealed record FileOperation(Guid Id, string State, Guid SourceRoot, Guid TargetRoot, string SourceBase, string TargetBase,
    string SourcePath, string TargetPath, string FileId, string ContentId, long Size, byte[] Sha256, string SeriesKey,
    string? SeriesTitle, string? Volume, string? Chapter, string? Message);

internal sealed partial class LibraryStore
{
    private const string OperationColumns = """
        id, state, source_root, target_root, source_base, target_base, source_path, target_path,
        file_id, content_id, size, sha256, series_key, series_title, volume_hint, chapter_hint, message
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
                target_path, file_id, content_id, size, sha256, series_key, series_title, volume_hint, chapter_hint, created_at, updated_at)
            VALUES($id, 'planned', $sourceRoot, $targetRoot, $sourceBase, $targetBase, $sourcePath,
                $targetPath, $file, $content, $size, $sha256, $series, $title, $volume, $chapter, $now, $now);
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
                AND NOT EXISTS (SELECT 1 FROM library_root r WHERE r.id IN (op.source_root, op.target_root) AND r.state IN ('queued', 'scanning'));
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

    public void FinishFileOperation(FileOperation operation, bool restore, DateTimeOffset modified, bool retainTarget, CoverSource? cover)
    {
        var root = restore ? operation.SourceRoot : operation.TargetRoot;
        var path = restore ? operation.SourcePath : operation.TargetPath;
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
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
        if (cover is not null)
        {
            command.Parameters.AddWithValue("$coverSeries", operation.SeriesKey);
            command.Parameters.AddWithValue("$coverSize", cover.Size);
            command.Parameters.AddWithValue("$coverEntry", cover.ArchiveEntry!);
            command.Parameters.AddWithValue("$coverHash", cover.Sha256);
            command.CommandText = """
                INSERT OR IGNORE INTO library_series_cover(root_id, series_key, relative_path, size, modified_at, archive_entry, sha256)
                VALUES($root, $coverSeries, $path, $coverSize, $modified, $coverEntry, $coverHash);
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static FileOperation ReadOperation(SqliteDataReader rows) => new(rows.GetGuid(0), rows.GetString(1), rows.GetGuid(2),
        rows.GetGuid(3), rows.GetString(4), rows.GetString(5), rows.GetString(6), rows.GetString(7), rows.GetString(8), rows.GetString(9),
        rows.GetInt64(10), rows.GetFieldValue<byte[]>(11), rows.GetString(12), ReadText(rows, 13), ReadText(rows, 14),
        ReadText(rows, 15), ReadText(rows, 16));

    private static void AddSeriesParameters(SqliteCommand command, FileOperation operation)
    {
        command.Parameters.AddWithValue("$series", operation.SeriesKey);
        command.Parameters.AddWithValue("$title", (object?)operation.SeriesTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$volume", (object?)operation.Volume ?? DBNull.Value);
        command.Parameters.AddWithValue("$chapter", (object?)operation.Chapter ?? DBNull.Value);
    }
}
