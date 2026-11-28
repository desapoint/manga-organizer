using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record CatalogAniList(SeriesMetadata Metadata, string? Revision, bool Close);
internal sealed record CatalogSeries(Guid RootId, string Key, string Title, long FileCount, bool Suggested, bool HasCover, bool HasMetadata,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CatalogAniList? AniList = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool LookupQueued = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CoverSeriesKey = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LookupTitle = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SeriesFileReview>? ReviewFiles = null);

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

    private static string SeriesQuery(string? view = null, bool review = false)
    {
        var published = view == "library";
        var files = review ? """
            , json_group_array(json_object('FileId', f.id, 'ContentId', f.content_id) ORDER BY f.id)
                  FILTER (WHERE f.content_id IS NOT NULL) AS review_files
            """ : "";
        var groups = """
        WITH file_groups AS (
            SELECT f.root_id, f.series_key, MIN(COALESCE(f.series_title, f.relative_path)) AS path_title, COUNT(*) AS file_count,
                MAX(f.series_title IS NOT NULL) AS suggested, MAX(f.modified_at) AS latest,
                EXISTS(SELECT 1 FROM library_series_cover cover WHERE cover.root_id = f.root_id AND cover.series_key = f.series_key) AS has_cover,
        """ + " " + SharedSeriesId + " AS linked_metadata" + files + """

            FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            LEFT JOIN content_metadata cm ON cm.content_id = f.content_id
            LEFT JOIN series_metadata linked ON linked.id = cm.metadata_id AND
        """ + " " + LinkedSeriesScope("f", "linked") + """

            WHERE (f.root_id = $id OR $id IS NULL AND folder.kind IN ('library', 'both'))
        """ + (published ? "" : " AND ($seriesKey IS NULL OR f.series_key = $seriesKey)") + CatalogVisibility(view) + """

            GROUP BY f.root_id, f.series_key
        ), local_series AS (
            SELECT g.root_id, g.series_key, m.provider_details, m.manual_tags, COALESCE(m.manual_tags, m.provider_details -> '$.Tags') AS tags, COALESCE(m.title, g.path_title) AS title, g.path_title, m.alternate_title, m.alternate_titles, g.file_count,
                g.suggested AND m.id IS NULL AS suggested, g.latest, g.has_cover OR EXISTS(
                    SELECT 1 FROM series_cover_preference pref
                    WHERE pref.series_id = m.id
                ) AS has_cover, m.id IS NOT NULL AS has_metadata,
                m.kind, m.summary, m.summary_markup, m.status, m.total_chapters, m.total_volumes, m.provider_id, g.linked_metadata AS shared_work,
                binding.metadata_id IS NULL AND NOT EXISTS (
                    SELECT 1 FROM library_file peer JOIN content_metadata linked ON linked.content_id = peer.content_id
                    WHERE peer.root_id = g.root_id AND peer.series_key = g.series_key) AS can_confirm_match
        """ + (review ? ", g.review_files" : "") + """
            FROM file_groups g LEFT JOIN series_metadata_binding binding ON binding.root_id = g.root_id AND binding.series_key = g.series_key
            LEFT JOIN series_metadata m ON m.id = COALESCE(binding.metadata_id, g.linked_metadata)
        )
        """;

        return groups + (published ? """
            , published_members AS (
                SELECT s.*, MIN(series_key) OVER cohort AS library_key,
                    SUM(file_count) OVER cohort AS library_count, MAX(latest) OVER cohort AS library_latest,
                    MIN(CASE WHEN has_cover THEN series_key END) OVER cohort AS cover_key
                FROM local_series s
                WINDOW cohort AS (PARTITION BY root_id, shared_work, CASE WHEN shared_work IS NULL THEN series_key END)
            ), folder_series AS (
                SELECT root_id, series_key, tags, title, library_count AS file_count,
                    suggested, library_latest AS latest, cover_key IS NOT NULL AS has_cover, has_metadata, cover_key
                FROM published_members WHERE series_key = library_key AND ($seriesKey IS NULL OR EXISTS(
                    SELECT 1 FROM published_members member WHERE member.root_id = published_members.root_id
                        AND member.library_key = published_members.library_key AND member.series_key = $seriesKey))
            )
            """ : ", folder_series AS (SELECT *, NULL AS cover_key FROM local_series)");
    }

    public CatalogPage<CatalogSeries> ReadSeries(Guid? id, int offset, int limit, SeriesFilters filters, string? sort = null,
        string? seriesKey = null, Guid? manualCollection = null, string? view = null)
    {
        using var database = view is "cover" or "attention" ? OpenCoverDatabase() : OpenDatabase();
        RegisterTitleSearch(database);
        database.CreateFunction<string?, string, bool>("tag_equals",
            (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase), isDeterministic: true);
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        var published = view == "library";
        static string TitleMatch(string alias) => $"""
            ($search = '' OR title_contains({alias}.title, $search) OR title_contains({alias}.path_title, $search)
                OR {AlternateTitleSearch(alias)})
            """;
        const string collectionMatch = """
            ($collection IS NULL OR EXISTS (SELECT 1 FROM series_collection_item i
                WHERE i.collection_id = $collection AND i.root_id = candidate.root_id AND i.series_key = candidate.series_key))
            """;
        var filter = """
             FROM folder_series candidate WHERE ($coverOnly = 0 OR has_cover)
            AND NOT EXISTS (SELECT 1 FROM json_each($includeTags) wanted
                WHERE NOT EXISTS (SELECT 1 FROM json_each(tags) tag
                    WHERE tag_equals(json_extract(tag.value, '$.Name'), wanted.value)))
            AND NOT EXISTS (SELECT 1 FROM json_each(tags) tag
                JOIN json_each($excludeTags) excluded ON tag_equals(json_extract(tag.value, '$.Name'), excluded.value))
            """ + (published ? """
                 AND ($search = '' OR EXISTS(SELECT 1 FROM published_members candidate_member
                    WHERE candidate_member.root_id = candidate.root_id AND candidate_member.library_key = candidate.series_key AND
                """ + " " + TitleMatch("candidate_member") + """
                ))
                 AND ($collection IS NULL OR EXISTS (SELECT 1 FROM series_collection_item i JOIN published_members member
                    ON member.root_id = i.root_id AND member.series_key = i.series_key
                    WHERE i.collection_id = $collection AND member.root_id = candidate.root_id AND member.library_key = candidate.series_key))
                """ : " AND " + TitleMatch("candidate") + " AND " + collectionMatch);
        var includeMatches = view is "all" or "attention" or "association" or "series" or "metadata" or "cover" or "close";
        var query = SeriesQuery(view, review: includeMatches);
        command.CommandText = query + " SELECT COUNT(*)" + filter;
        command.Parameters.AddWithValue("$id", (object?)id ?? DBNull.Value);
        command.Parameters.AddWithValue("$seriesKey", (object?)seriesKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$search", filters.Search);
        command.Parameters.AddWithValue("$coverOnly", filters.CoverOnly);
        command.Parameters.AddWithValue("$includeTags", JsonSerializer.Serialize(filters.IncludeTags));
        command.Parameters.AddWithValue("$excludeTags", JsonSerializer.Serialize(filters.ExcludeTags));
        command.Parameters.AddWithValue("$collection", (object?)manualCollection ?? DBNull.Value);
        var total = (long)command.ExecuteScalar()!;
        var lookupQueued = false;
        if (view == "close")
        {
            command.CommandText = """
                SELECT EXISTS (SELECT 1 FROM metadata_suggestion s JOIN library_file f ON f.series_key = s.series_key
                    WHERE s.queued = 1 AND f.root_id = $id);
                """;
            lookupQueued = (long)command.ExecuteScalar()! != 0;
        }
        var order = sort switch { "recent" => "latest DESC, ", "files" => "file_count DESC, ", _ => "" };
        const string matchFields = """
            , alternate_title, kind, summary, status, total_chapters, total_volumes, provider_id, provider_details,
                (SELECT revision FROM metadata_suggestion s WHERE s.series_key = candidate.series_key),
                (SELECT candidates FROM metadata_suggestion s WHERE s.series_key = candidate.series_key),
                COALESCE((SELECT queued FROM metadata_suggestion s WHERE s.series_key = candidate.series_key), 0), can_confirm_match, manual_tags, review_files,
                alternate_titles, summary_markup
            """;
        command.CommandText = query + " SELECT root_id, series_key, title, file_count, suggested, has_cover, has_metadata"
            + (includeMatches ? matchFields : ", cover_key") + filter +
            " ORDER BY " + order + "title COLLATE NOCASE, series_key, root_id LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<CatalogSeries>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var title = rows.GetString(2);
            var matched = includeMatches ? ReadCatalogAniList(rows, title) : null;
            var suggested = rows.GetBoolean(4);
            var hasMetadata = rows.GetBoolean(6);
            string? lookupTitle = null;
            if (includeMatches && !hasMetadata)
            {
                lookupTitle = suggested ? DoujinFilename.LookupOverride(title)
                    : DoujinFilename.Parse(Path.GetFileNameWithoutExtension(title))?.Title ?? "";
            }
            var displayTitle = lookupTitle switch { null => title, "" => "Unidentified series", _ => lookupTitle };
            items.Add(new(rows.GetGuid(0), rows.GetString(1), displayTitle, rows.GetInt64(3), suggested, rows.GetBoolean(5),
                hasMetadata, matched, includeMatches && rows.GetBoolean(18) && rows.GetBoolean(17),
                published ? ReadText(rows, 7) : null, lookupTitle,
                includeMatches ? JsonSerializer.Deserialize<SeriesFileReview[]>(rows.GetString(20)) ?? [] : null));
        }

        return new(total, items, lookupQueued);
    }

    private static CatalogAniList? ReadCatalogAniList(SqliteDataReader rows, string title)
    {
        if (ReadInt(rows, 13) is { } provider)
        {
            var details = ReadText(rows, 14) is { } json ? JsonSerializer.Deserialize<AniListDetails>(json) : null;
            return new(new(title, rows.GetString(8), ReadText(rows, 7), MetadataAgent.PlainDescription(ReadText(rows, 9)),
                ReadText(rows, 10), ReadInt(rows, 11), ReadInt(rows, 12), provider, details, ReadManualTags(rows, 19))
                { AlternateTitles = ReadAlternateTitles(rows, 21), SummaryMarkup = ReadText(rows, 22) }, null, false);
        }
        if (!rows.GetBoolean(18) || ReadText(rows, 15) is not { } revision || ReadText(rows, 16) is not { } candidates) return null;
        if (JsonSerializer.Deserialize<MetadataProposal[]>(candidates) is not { Length: > 0 } proposals) return null;
        var exact = proposals.Where(proposal => proposal.MatchingTitle is not null).ToArray();
        return exact.Length == 1 ? new(exact[0].Match.ToMetadata(), revision, true)
            : proposals.Length == 1 ? new(proposals[0].Match.ToMetadata(), revision, false) : null;
    }

    public IReadOnlyList<string> ReadSeriesTags(Guid? root)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = SeriesQuery() + """
             SELECT DISTINCT json_extract(tag.value, '$.Name') COLLATE NOCASE AS name
             FROM folder_series, json_each(tags) tag
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

    private static string AlternateTitleSearch(string alias) => $"""
        EXISTS(SELECT 1 FROM json_each({alias}.alternate_titles) alternate WHERE title_contains(alternate.value, $search))
        """;

    private static void RegisterTitleSearch(SqliteConnection database) =>
        database.CreateFunction<string, string, bool>("title_contains", (title, query) =>
            CultureInfo.InvariantCulture.CompareInfo.IndexOf(title, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0,
            isDeterministic: true);
}
