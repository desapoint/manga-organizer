using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record SeriesCoverCandidate(string Id, string Label);
internal sealed record SeriesCoverChoices(long Total, IReadOnlyList<SeriesCoverCandidate> Items, string? PreferredLabel, string? AniListCoverUrl);
internal sealed record SeriesCoverPicture(byte[]? Data, string? ProviderUrl, string? Label = null);

internal sealed partial class LibraryStore
{
    private static SeriesCoverPicture? ReadPreferredCover(SqliteCommand command)
    {
        command.CommandText = "SELECT data, provider_url, label FROM series_cover_preference WHERE series_id = " + MetadataIdForGroup;
        using var rows = command.ExecuteReader();
        if (!rows.Read()) return null;

        var data = rows.IsDBNull(0) ? null : rows.GetFieldValue<byte[]>(0);
        var url = MetadataAgent.CoverUrl(ReadText(rows, 1));
        return data is null && url is null ? null : new(data, url, rows.GetString(2));
    }

    private static string CoverCandidates(bool published) => "WITH linked_files AS MATERIALIZED (" + """
            SELECT f.root_id, f.series_key, f.relative_path, f.content_id
            FROM content_metadata cm JOIN library_file f ON f.content_id = cm.content_id
            JOIN series_metadata m ON m.id = cm.metadata_id AND
        """ + " " + LinkedSeriesScope("f", "m") + """
             JOIN library_folder folder ON folder.root_id = f.root_id
            WHERE cm.metadata_id =
        """ + " " + MetadataIdForGroup + (published ? " AND folder.kind IN ('library', 'both')" : "") + """
            UNION
            SELECT f.root_id, f.series_key, f.relative_path, f.content_id FROM library_file f
            WHERE f.root_id = $root AND
        """ + " " + GroupSeriesKeys + SeriesVisibility(published) + """
        ), sources AS (
            SELECT art.image_sha, f.relative_path AS label
            FROM linked_files f JOIN library_content content ON content.id = f.content_id
            JOIN artwork.archive_cover art ON art.archive_sha = content.sha256
            UNION ALL
            SELECT f.image_sha, f.relative_path FROM artwork.folder_cover f
            WHERE EXISTS (SELECT 1 FROM linked_files linked WHERE linked.root_id = f.root_id
                AND linked.series_key = f.series_key)
        ), candidates AS (
            SELECT s.image_sha, MIN(s.label) AS label FROM sources s JOIN artwork.image image ON image.sha256 = s.image_sha
            GROUP BY s.image_sha
        )
        """;

    public SeriesCoverChoices? ReadCoverChoices(Guid root, string seriesKey, bool published, int offset, int limit)
    {
        using var database = OpenCoverDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, seriesKey, published);
        if (!GroupExists(command, published)) return null;

        var preferred = ReadPreferredCover(command);
        var provider = ReadMetadata(command)?.ProviderDetails?.CoverUrl;
        var candidates = CoverCandidates(published);
        command.CommandText = candidates + " SELECT COUNT(*) FROM candidates;";
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = candidates + """
             SELECT lower(hex(image_sha)), label FROM candidates ORDER BY label COLLATE NOCASE, image_sha
             LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<SeriesCoverCandidate>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read()) items.Add(new(rows.GetString(0), rows.GetString(1)));
        return new(total, items, preferred?.Label, MetadataAgent.CoverUrl(provider));
    }

    private static SeriesCoverPicture? ReadCoverCandidate(SqliteCommand command, string id, bool published)
    {
        command.Parameters.AddWithValue("$candidate", Convert.FromHexString(id));
        command.CommandText = CoverCandidates(published) + """
             SELECT image.data, c.label FROM candidates c JOIN artwork.image image ON image.sha256 = c.image_sha
             WHERE c.image_sha = $candidate;
            """;
        using var rows = command.ExecuteReader();
        return rows.Read() ? new(rows.GetFieldValue<byte[]>(0), null, rows.GetString(1)) : null;
    }

    public byte[]? ReadCoverCandidate(Guid root, string seriesKey, string candidate, bool published)
    {
        using var database = OpenCoverDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, seriesKey, published);
        return GroupExists(command, published) ? ReadCoverCandidate(command, candidate, published)?.Data : null;
    }

    public bool SetSeriesCover(Guid root, string seriesKey, bool published, string? candidate = null, byte[]? upload = null, bool useProvider = false)
    {
        using var database = OpenCoverDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, seriesKey, published);
        if (!GroupExists(command, published)) return false;
        command.CommandText = "SELECT " + MetadataIdForGroup;
        if (command.ExecuteScalar() is not string seriesId) return false;
        command.Parameters.AddWithValue("$coverSeries", seriesId);

        if (candidate is null && upload is null && !useProvider)
        {
            command.CommandText = "DELETE FROM series_cover_preference WHERE series_id = $coverSeries;";
        }
        else
        {
            var picture = upload is not null ? new SeriesCoverPicture(upload, null, "Uploaded image")
                : candidate is not null ? ReadCoverCandidate(command, candidate, published)
                : MetadataAgent.CoverUrl(ReadMetadata(command)?.ProviderDetails?.CoverUrl) is { } url
                    ? new(null, url, "AniList cover") : null;
            if (picture is null) return false;

            command.CommandText = """
                INSERT INTO series_cover_preference(series_id, label, data, provider_url)
                VALUES ($coverSeries, $label, $data, $url)
                ON CONFLICT(series_id) DO UPDATE
                    SET label = excluded.label, data = excluded.data, provider_url = excluded.provider_url;
                """;
            command.Parameters.AddWithValue("$label", picture.Label!);
            command.Parameters.AddWithValue("$data", (object?)picture.Data ?? DBNull.Value);
            command.Parameters.AddWithValue("$url", (object?)picture.ProviderUrl ?? DBNull.Value);
        }
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }
}
