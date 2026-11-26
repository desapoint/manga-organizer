using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record CatalogSeries(Guid RootId, string Key, string Title, long FileCount, bool Suggested, bool HasCover, bool HasMetadata);

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

    private const string SeriesQuery = """
        WITH file_groups AS (
            SELECT f.root_id, f.series_key, MIN(COALESCE(f.series_title, f.relative_path)) AS path_title, COUNT(*) AS file_count,
                MAX(f.series_title IS NOT NULL) AS suggested, MAX(f.modified_at) AS latest,
                EXISTS(SELECT 1 FROM library_series_cover cover WHERE cover.root_id = f.root_id AND cover.series_key = f.series_key) AS has_cover,
                CASE WHEN COUNT(DISTINCT cm.metadata_id) = 1 THEN MIN(cm.metadata_id) END AS linked_metadata
            FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            LEFT JOIN content_metadata cm ON cm.content_id = f.content_id
            WHERE (f.root_id = $id OR $id IS NULL AND folder.kind IN ('library', 'both'))
                AND ($seriesKey IS NULL OR f.series_key = $seriesKey) GROUP BY f.root_id, f.series_key
        ), folder_series AS (
            SELECT g.root_id, g.series_key, m.provider_details, COALESCE(m.title, g.path_title) AS title, g.path_title, m.alternate_title, g.file_count,
                g.suggested AND m.id IS NULL AS suggested, g.latest, g.has_cover, m.id IS NOT NULL AS has_metadata
            FROM file_groups g LEFT JOIN series_metadata_binding binding ON binding.root_id = g.root_id AND binding.series_key = g.series_key
            LEFT JOIN series_metadata m ON m.id = COALESCE(binding.metadata_id, g.linked_metadata)
        )
        """;

    public CatalogPage<CatalogSeries> ReadSeries(Guid? id, int offset, int limit, SeriesFilters filters, string? sort = null,
        string? seriesKey = null, Guid? manualCollection = null)
    {
        using var database = OpenDatabase();
        RegisterTitleSearch(database);
        database.CreateFunction<string?, string, bool>("tag_equals",
            (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase), isDeterministic: true);
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        const string filter = """
             FROM folder_series WHERE ($coverOnly = 0 OR has_cover) AND ($search = '' OR title_contains(title, $search)
                 OR title_contains(path_title, $search) OR title_contains(COALESCE(alternate_title, ''), $search))
            AND NOT EXISTS (SELECT 1 FROM json_each($includeTags) wanted
                WHERE NOT EXISTS (SELECT 1 FROM json_each(provider_details, '$.Tags') tag
                    WHERE tag_equals(json_extract(tag.value, '$.Name'), wanted.value)))
            AND NOT EXISTS (SELECT 1 FROM json_each(provider_details, '$.Tags') tag
                JOIN json_each($excludeTags) excluded ON tag_equals(json_extract(tag.value, '$.Name'), excluded.value))
            AND ($collection IS NULL OR EXISTS (SELECT 1 FROM series_collection_item i
                WHERE i.collection_id = $collection AND i.root_id = folder_series.root_id AND i.series_key = folder_series.series_key))
            """;
        command.CommandText = SeriesQuery + " SELECT COUNT(*)" + filter;
        command.Parameters.AddWithValue("$id", (object?)id ?? DBNull.Value);
        command.Parameters.AddWithValue("$seriesKey", (object?)seriesKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$search", filters.Search);
        command.Parameters.AddWithValue("$coverOnly", filters.CoverOnly);
        command.Parameters.AddWithValue("$includeTags", JsonSerializer.Serialize(filters.IncludeTags));
        command.Parameters.AddWithValue("$excludeTags", JsonSerializer.Serialize(filters.ExcludeTags));
        command.Parameters.AddWithValue("$collection", (object?)manualCollection ?? DBNull.Value);
        var total = (long)command.ExecuteScalar()!;
        var order = sort switch { "recent" => "latest DESC, ", "files" => "file_count DESC, ", _ => "" };
        command.CommandText = SeriesQuery + " SELECT root_id, series_key, title, file_count, suggested, has_cover, has_metadata" + filter +
            " ORDER BY " + order + "title COLLATE NOCASE, series_key, root_id LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<CatalogSeries>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            items.Add(new(rows.GetGuid(0), rows.GetString(1), rows.GetString(2), rows.GetInt64(3), rows.GetBoolean(4), rows.GetBoolean(5), rows.GetBoolean(6)));
        }

        return new(total, items);
    }

    public IReadOnlyList<string> ReadSeriesTags(Guid? root)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = SeriesQuery + """
             SELECT DISTINCT json_extract(tag.value, '$.Name') COLLATE NOCASE AS name
             FROM folder_series, json_each(provider_details, '$.Tags') tag
             WHERE COALESCE(json_extract(tag.value, '$.Spoiler'), 0) = 0 AND name IS NOT NULL
             ORDER BY name;
            """;
        command.Parameters.AddWithValue("$id", (object?)root ?? DBNull.Value);
        command.Parameters.AddWithValue("$seriesKey", DBNull.Value);
        var tags = new List<string>();
        using var rows = command.ExecuteReader();
        while (rows.Read()) tags.Add(rows.GetString(0));
        return tags;
    }

    private static void RegisterTitleSearch(SqliteConnection database) =>
        database.CreateFunction<string, string, bool>("title_contains", (title, query) =>
            CultureInfo.InvariantCulture.CompareInfo.IndexOf(title, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0,
            isDeterministic: true);
}
