namespace MangaOrganizer.Server;

internal sealed record ReleaseMetadata(string? Title, string? Summary);

internal sealed partial class LibraryStore
{
    public ReleaseMetadata? ReadReleaseMetadata(Guid root, string series, string volume, string chapter)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, series, published: true);
        if (!GroupExists(command, published: true)) return null;
        command.Parameters.AddWithValue("$volume", volume);
        command.Parameters.AddWithValue("$chapter", chapter);
        command.CommandText = """
            SELECT title, summary FROM series_release_metadata
            WHERE volume = $volume AND chapter = $chapter AND series_id =
            """ + " " + MetadataIdForGroup;
        using var rows = command.ExecuteReader();
        return rows.Read() ? new(ReadText(rows, 0), ReadText(rows, 1)) : new(null, null);
    }

    public bool SaveReleaseMetadata(Guid root, string series, string volume, string chapter, ReleaseMetadata metadata)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction();
        using var command = GroupCommand(database, transaction, root, series, published: true);
        if (!GroupExists(command, published: true)) return false;
        command.CommandText = "SELECT " + MetadataIdForGroup;
        if (command.ExecuteScalar() is not string id) return false;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$volume", volume);
        command.Parameters.AddWithValue("$chapter", chapter);
        command.Parameters.AddWithValue("$title", (object?)metadata.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", (object?)metadata.Summary ?? DBNull.Value);
        command.CommandText = metadata.Title is null && metadata.Summary is null
            ? "DELETE FROM series_release_metadata WHERE series_id = $id AND volume = $volume AND chapter = $chapter;"
            : """
                INSERT INTO series_release_metadata(series_id, volume, chapter, title, summary)
                VALUES ($id, $volume, $chapter, $title, $summary)
                ON CONFLICT(series_id, volume, chapter) DO UPDATE SET title = excluded.title, summary = excluded.summary;
                """;
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }
}
