using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed partial class LibraryStore
{
    private const string IntakeNumbered = """
        (f.volume_hint IS NOT NULL OR f.chapter_hint IS NOT NULL
            OR EXISTS(SELECT 1 FROM content_chapter chapter WHERE chapter.content_id = f.content_id)
            OR EXISTS(SELECT 1 FROM inferred_volume_chapters volume WHERE volume.content_id = f.content_id)
            OR (
        """ + " " + FinishedOneShot + "))";

    private const string IntakeTransferCandidates = """
        SELECT 1 FROM library_file f JOIN library_intake_target route ON route.source_root = f.root_id
        JOIN library_folder source ON source.root_id = route.source_root
        JOIN library_folder target ON target.root_id = route.target_root
        JOIN library_root source_status ON source_status.id = source.root_id
        JOIN library_root target_status ON target_status.id = target.root_id
        WHERE source.kind IN ('import', 'both') AND target.kind IN ('library', 'both')
            AND source.managed = 1 AND target.managed = 1
            AND source_status.state NOT IN ('queued', 'scanning', 'unavailable')
            AND target_status.state NOT IN ('queued', 'scanning', 'unavailable')
            AND
        """ + " " + ConfirmedSeries + " AND " + IntakeNumbered;

    public bool SetIntakeTarget(Guid source, Guid? target)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        if (!CanChangeFolder(command, source)) return false;

        command.CommandText = "SELECT 1 FROM library_folder WHERE root_id = $id AND kind IN ('import', 'both');";
        if (command.ExecuteScalar() is null)
            throw new ArgumentException("Choose a folder configured for Import intake.");

        if (target is { } destination)
        {
            command.Parameters.AddWithValue("$target", destination);
            command.CommandText = "SELECT 1 FROM library_folder WHERE root_id = $target AND kind IN ('library', 'both');";
            if (command.ExecuteScalar() is null)
                throw new ArgumentException("Choose a configured library folder as the intake target.");
            command.CommandText = """
                INSERT INTO library_intake_target(source_root, target_root) VALUES ($id, $target)
                ON CONFLICT(source_root) DO UPDATE SET target_root = excluded.target_root;
                """;
        }
        else
        {
            command.CommandText = "DELETE FROM library_intake_target WHERE source_root = $id;";
        }
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public bool CanPreviewIntakeTransfer(Guid source, Guid target, string fileId, string contentId)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = IntakeTransferCandidates + """

            AND f.root_id = $source AND route.target_root = $target AND f.id = $file AND f.content_id = $content;
            """;
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$file", fileId);
        command.Parameters.AddWithValue("$content", contentId);
        return command.ExecuteScalar() is not null;
    }

    public bool RequiresIntakeTransfer(Guid source)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT 1 FROM library_folder WHERE root_id = $source AND kind = 'import';";
        command.Parameters.AddWithValue("$source", source);
        return command.ExecuteScalar() is not null;
    }

    private static bool GroupHasActiveIntakeTransfer(SqliteCommand command)
    {
        command.CommandText = """
            SELECT 1 FROM library_file f JOIN file_operation op ON op.content_id = f.content_id
            WHERE f.root_id = $root AND f.series_key = $series AND op.intake_transfer = 1 AND op.state = 'applying' LIMIT 1;
            """;
        return command.ExecuteScalar() is not null;
    }
}
