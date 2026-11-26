using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record CatalogSeries(string Key, string Title, long FileCount);

internal sealed partial class LibraryStore
{
    private static void InitializeSeries(SqliteConnection database, SqliteCommand command)
    {
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('library_file') WHERE name = 'series_key';";
        if ((long)command.ExecuteScalar()! == 0)
        {
            database.CreateFunction<string, string, string?>("series_candidate", SeriesSuggestions.CandidateTitle, isDeterministic: true);
            database.CreateFunction<string, string?, string>("series_key_for", SeriesSuggestions.Key, isDeterministic: true);
            command.CommandText = """
                ALTER TABLE library_file ADD COLUMN series_key TEXT;
                ALTER TABLE library_file ADD COLUMN series_title TEXT;
                WITH candidates AS MATERIALIZED (
                    SELECT f.id, series_candidate(f.relative_path, COALESCE(folder.path, root.path_key)) AS title
                    FROM library_file f JOIN library_root root ON root.id = f.root_id
                    LEFT JOIN library_folder folder ON folder.root_id = root.id
                )
                UPDATE library_file AS f SET series_title = candidates.title,
                    series_key = series_key_for(f.relative_path, candidates.title)
                FROM candidates WHERE f.id = candidates.id;
                """;
            command.ExecuteNonQuery();
        }

        command.CommandText = "CREATE INDEX IF NOT EXISTS library_file_series ON library_file(root_id, series_key, relative_path);";
        command.ExecuteNonQuery();
    }

    public CatalogPage<CatalogSeries> ReadSeries(Guid id, int offset, int limit)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(DISTINCT series_key) FROM library_file WHERE root_id = $id;";
        command.Parameters.AddWithValue("$id", id);
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = """
            SELECT series_key, MIN(COALESCE(series_title, relative_path)) AS title, COUNT(*)
            FROM library_file WHERE root_id = $id
            GROUP BY series_key ORDER BY title COLLATE NOCASE, series_key LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<CatalogSeries>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            items.Add(new(rows.GetString(0), rows.GetString(1), rows.GetInt64(2)));
        }

        return new(total, items);
    }
}
