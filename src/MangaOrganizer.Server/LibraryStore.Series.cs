using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record CatalogSeries(string Key, string Title, long FileCount, bool Suggested, bool HasCover);

internal sealed partial class LibraryStore
{
    internal static void InitializeSeries(SqliteConnection database, SqliteCommand command)
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

    public CatalogPage<CatalogSeries> ReadSeries(Guid id, int offset, int limit, string search, string? sort, bool coverOnly, string? seriesKey = null)
    {
        using var database = OpenDatabase();
        database.CreateFunction<string, string, bool>("title_contains", (title, query) =>
            CultureInfo.InvariantCulture.CompareInfo.IndexOf(title, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0,
            isDeterministic: true);
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        const string seriesQuery = """
            WITH folder_series AS (
                SELECT f.series_key, MIN(COALESCE(f.series_title, f.relative_path)) AS title, COUNT(*) AS file_count,
                    MAX(f.series_title IS NOT NULL) AS suggested, MAX(f.modified_at) AS latest,
                    EXISTS(SELECT 1 FROM library_series_cover cover WHERE cover.root_id = $id AND cover.series_key = f.series_key) AS has_cover
                FROM library_file f WHERE f.root_id = $id AND ($seriesKey IS NULL OR f.series_key = $seriesKey) GROUP BY f.series_key
            )
            """;
        const string filter = " FROM folder_series WHERE ($coverOnly = 0 OR has_cover) AND ($search = '' OR title_contains(title, $search))";
        command.CommandText = seriesQuery + " SELECT COUNT(*)" + filter;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$seriesKey", (object?)seriesKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$coverOnly", coverOnly);
        var total = (long)command.ExecuteScalar()!;
        var order = sort switch { "recent" => "latest DESC, ", "files" => "file_count DESC, ", _ => "" };
        command.CommandText = seriesQuery + " SELECT series_key, title, file_count, suggested, has_cover" + filter +
            " ORDER BY " + order + "title COLLATE NOCASE, series_key LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<CatalogSeries>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            items.Add(new(rows.GetString(0), rows.GetString(1), rows.GetInt64(2), rows.GetBoolean(3), rows.GetBoolean(4)));
        }

        return new(total, items);
    }
}
