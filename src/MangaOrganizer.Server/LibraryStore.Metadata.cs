using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record SeriesMetadata(string Title, string Kind, string? AlternateTitle, string? Summary, string? Status,
    int? TotalChapters, int? TotalVolumes, int? ProviderId, AniListDetails? ProviderDetails)
{
    public string? ProviderUrl => ProviderId is { } id ? MetadataAgent.ListingUrl(id) : null;
}
internal sealed record SeriesChapter(string? Volume, string Chapter,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null);
internal sealed record LibraryChapter(string? Volume, string Chapter, string? ContentId, int ReadableFiles, bool Completed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreferredContentId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Missing = false);
internal sealed record SeriesReading(string ContentId, string RelativePath, int? PageIndex, int? PageCount, bool HasFinishedFile);
internal sealed record VolumeReading(string Volume, string ContentId, int ReleaseCount);
internal sealed record SeriesDetails(SeriesMetadata? Metadata, int AvailableChapters, int ReadChapters, IReadOnlyList<string> Volumes,
    IReadOnlyList<LibraryChapter> Chapters, SeriesReading? Reading, int IndexedVolumes, IReadOnlyList<string> ReadableVolumes,
    IReadOnlyList<VolumeReading> VolumeReading, int ReadCatalogChapters);

internal sealed partial class LibraryStore
{
    private const string MetadataIdForGroup = """
        COALESCE(
            (SELECT binding.metadata_id FROM series_metadata_binding binding JOIN series_metadata confirmed ON confirmed.id = binding.metadata_id
                WHERE binding.root_id = $root AND binding.series_key = $series AND confirmed.series_key = $series),
            (SELECT CASE WHEN COUNT(DISTINCT cm.metadata_id) = 1 THEN MIN(cm.metadata_id) END
                FROM library_file f JOIN content_metadata cm ON cm.content_id = f.content_id
                JOIN series_metadata confirmed ON confirmed.id = cm.metadata_id
                WHERE f.root_id = $root AND f.series_key = $series AND confirmed.series_key = $series)
        )
        """;

    private const string FinishedOneShot = """
        f.chapter_hint IS NULL AND EXISTS (
            SELECT 1 FROM content_metadata association JOIN series_metadata metadata ON metadata.id = association.metadata_id
            WHERE association.content_id = f.content_id AND metadata.series_key = f.series_key AND metadata.provider_id IS NOT NULL
                AND metadata.status = 'FINISHED' COLLATE NOCASE AND metadata.total_chapters = 1)
        """;

    private const string GroupCoverage = $$"""
        SELECT f.content_id, c.volume, c.chapter FROM library_file f JOIN content_chapter c ON c.content_id = f.content_id
        WHERE f.root_id = $root AND {{GroupSeriesKeys}}
        UNION
        SELECT f.content_id, inferred.volume, chapter.value
        FROM library_file f JOIN active_inferred_volume_chapters inferred ON inferred.content_id = f.content_id, json_each(inferred.chapters) chapter
        WHERE f.root_id = $root AND {{GroupSeriesKeys}}
            AND NOT EXISTS (SELECT 1 FROM content_chapter c WHERE c.content_id = f.content_id)
        UNION
        SELECT f.content_id, COALESCE(f.volume_hint, ''), f.chapter_hint FROM library_file_numbered f
        WHERE f.root_id = $root AND {{GroupSeriesKeys}} AND f.content_id IS NOT NULL AND f.chapter_hint IS NOT NULL
            AND NOT EXISTS (SELECT 1 FROM content_chapter c WHERE c.content_id = f.content_id)
            AND NOT EXISTS (SELECT 1 FROM active_inferred_volume_chapters inferred WHERE inferred.content_id = f.content_id)
        UNION
        SELECT f.content_id, COALESCE(f.volume_hint, ''), '1' FROM library_file_numbered f
        WHERE f.root_id = $root AND {{GroupSeriesKeys}} AND f.content_id IS NOT NULL
            AND NOT EXISTS (SELECT 1 FROM content_chapter c WHERE c.content_id = f.content_id)
            AND NOT EXISTS (SELECT 1 FROM active_inferred_volume_chapters inferred WHERE inferred.content_id = f.content_id)
            AND {{FinishedOneShot}}
        """;

    internal static void BackfillBookNumbers(SqliteConnection database, SqliteCommand command)
    {
        database.CreateFunction<string, string?>("volume_hint_for", path => BookNumbers.FromPath(path).Volume, isDeterministic: true);
        database.CreateFunction<string, string?>("chapter_hint_for", path => BookNumbers.FromPath(path).Chapter, isDeterministic: true);
        command.CommandText = "UPDATE library_file SET volume_hint = volume_hint_for(relative_path), chapter_hint = chapter_hint_for(relative_path);";
        command.ExecuteNonQuery();
    }

    public SeriesDetails? ReadSeriesDetails(Guid root, string seriesKey, Guid owner, bool published)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, seriesKey, published);
        if (!GroupExists(command, published)) return null;

        var metadata = ReadMetadata(command);
        command.Parameters.AddWithValue("$owner", owner);
        AddWorkKey(command, root);
        var visible = SeriesVisibility(published);
        var coverage = ScopedGroupCoverage(published);
        command.CommandText = "WITH readable_files AS (" + ReadableGroupFiles + visible + """
            ), readable AS (SELECT DISTINCT content_id FROM readable_files)
            SELECT coverage.volume, coverage.chapter, COALESCE(chapter.completed, local.completed, related_status.completed, MAX(COALESCE(p.completed, 0))),
                COUNT(readable.content_id), MIN(readable.content_id),
                MAX(CASE WHEN readable.content_id = COALESCE(preferred.content_id, preferred_local.content_id) THEN readable.content_id END)
            FROM (
            """ + " " + coverage + """
            ) coverage LEFT JOIN reading_progress p ON p.owner_id = $owner AND p.content_id = coverage.content_id
            LEFT JOIN readable ON readable.content_id = coverage.content_id
            LEFT JOIN chapter_completion chapter ON chapter.owner_id = $owner AND chapter.work_key = $work
                AND chapter.volume = coverage.volume AND chapter.chapter = coverage.chapter
            LEFT JOIN chapter_completion local ON local.owner_id = $owner AND local.work_key = $localWork
                AND local.volume = coverage.volume AND local.chapter = coverage.chapter
            LEFT JOIN preferred_chapter_release preferred ON preferred.owner_id = $owner AND preferred.work_key = $work
                AND preferred.volume = coverage.volume AND preferred.chapter = coverage.chapter
            LEFT JOIN preferred_chapter_release preferred_local ON preferred_local.owner_id = $owner AND preferred_local.work_key = $localWork
                AND preferred_local.volume = coverage.volume AND preferred_local.chapter = coverage.chapter
            """ + " " + RelatedChapterState + """

            GROUP BY coverage.volume, coverage.chapter;
            """;
        var chapters = new List<LibraryChapter>();
        var readChapters = 0;
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                var volume = rows.GetString(0);
                var count = rows.GetInt32(3);
                chapters.Add(new(volume.Length == 0 ? null : volume, rows.GetString(1), count == 1 ? rows.GetString(4) : null, count, rows.GetBoolean(2), ReadText(rows, 5)));
                if (rows.GetBoolean(2)) readChapters++;
            }
        }

        command.CommandText = """
            SELECT DISTINCT volume_hint FROM library_file_numbered f WHERE root_id = $root AND
            """ + " " + GroupSeriesKeys + """
                 AND volume_hint IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM content_chapter c WHERE c.content_id = f.content_id)
                AND NOT EXISTS (SELECT 1 FROM active_inferred_volume_chapters inferred WHERE inferred.content_id = f.content_id)
            """ + visible;
        var volumes = chapters.Select(chapter => chapter.Volume).OfType<string>().ToHashSet(StringComparer.Ordinal);
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read()) volumes.Add(rows.GetString(0));
        }

        command.CommandText = "WITH covered AS (" + coverage + "), volume_archive AS (" + GroupVolumeArchives
            + "), readable AS (" + ReadableGroupFiles + visible + """
            )
            , volume_paths AS (
                SELECT a.volume, f.content_id, MIN(f.relative_path) AS relative_path
                FROM volume_archive a JOIN readable f ON f.content_id = a.content_id GROUP BY a.volume, f.content_id
            ), ranked AS (
                SELECT v.volume, v.content_id, COUNT(*) OVER (PARTITION BY v.volume) AS release_count,
                    ROW_NUMBER() OVER (PARTITION BY v.volume ORDER BY p.updated_at DESC, v.relative_path, v.content_id) AS priority
                FROM volume_paths v LEFT JOIN reading_progress p ON p.owner_id = $owner AND p.content_id = v.content_id
            )
            SELECT volume, content_id, release_count FROM ranked WHERE priority = 1;
            """;
        var volumeReading = new List<VolumeReading>();
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read()) volumeReading.Add(new(rows.GetString(0), rows.GetString(1), rows.GetInt32(2)));
        }
        var indexedVolumes = volumes.Count;
        command.CommandText = "SELECT DISTINCT volume FROM edition_volume_manifest WHERE work_key IN (" + GroupWorkKeys + ");";
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read()) volumes.Add(rows.GetString(0));
        }
        var listedVolumes = volumes.OrderBy(Numeric).ToArray();
        volumeReading.Sort((first, second) => Numeric(first.Volume).CompareTo(Numeric(second.Volume)));
        command.Parameters.AddWithValue("$released", metadata?.TotalChapters ?? 0);
        command.CommandText = ChapterCatalog(published) + " SELECT COUNT(*) FROM catalog WHERE completed;";
        var catalogRead = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        return new(metadata, chapters.Count, readChapters, listedVolumes,
            chapters.OrderBy(chapter => Numeric(chapter.Volume)).ThenBy(chapter => Numeric(chapter.Chapter)).Take(500).ToArray(),
            ReadSeriesReading(command, visible), indexedVolumes, volumeReading.Select(volume => volume.Volume).ToArray(), volumeReading, catalogRead);
    }

    private static SeriesReading? ReadSeriesReading(SqliteCommand command, string visible)
    {
        command.CommandText = """
            SELECT f.content_id, f.relative_path, f.series_title, p.page_index, p.page_count, COALESCE(p.completed, 0)
            FROM library_file f JOIN library_root r ON r.id = f.root_id
            LEFT JOIN reading_progress p ON p.owner_id = $owner AND p.content_id = f.content_id
            WHERE f.root_id = $root AND
            """ + " " + GroupSeriesKeys + """
                 AND r.state <> 'unavailable'
              AND f.content_id IS NOT NULL AND f.extension IN ('cbz', 'zip', 'cbr', 'rar')
            """ + visible + """

            ORDER BY p.updated_at DESC, f.relative_path;
            """;
        using var rows = command.ExecuteReader();
        SeriesReading? first = null;
        CoverOrder firstOrder = default;
        var finishedFile = false;
        while (rows.Read())
        {
            if (rows.GetBoolean(5))
            {
                finishedFile = true;
                continue;
            }
            var path = rows.GetString(1);
            if (!rows.IsDBNull(3)) return new(rows.GetString(0), path, rows.GetInt32(3), rows.GetInt32(4), finishedFile);
            var order = CoverOrder.ForFile(path, ReadText(rows, 2));
            if (first is not null && order.CompareTo(firstOrder) >= 0) continue;
            firstOrder = order;
            first = new(rows.GetString(0), path, null, null, false);
        }

        return first is null ? null : first with { HasFinishedFile = finishedFile };
    }

    public SeriesMetadata? SaveMetadata(Guid root, string seriesKey, SeriesMetadata metadata, bool published)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, seriesKey, published);
        if (!GroupExists(command, published) || GroupHasActiveIntakeTransfer(command)) return null;

        var saved = WriteMetadata(command, metadata, attachFiles: !published);
        transaction.Commit();
        return saved;
    }

    public bool ConfirmFileAssociation(Guid root, string seriesKey, string fileId, string contentId)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, seriesKey);
        command.Parameters.AddWithValue("$file", fileId);
        command.Parameters.AddWithValue("$content", contentId);
        command.CommandText = """
            INSERT INTO content_metadata(content_id, metadata_id)
            SELECT f.content_id, metadata.id FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            JOIN series_metadata metadata ON metadata.series_key = f.series_key AND metadata.id = (
            """ + " " + MetadataIdForGroup + """
            )
            WHERE f.root_id = $root AND f.series_key = $series AND f.id = $file AND f.content_id = $content
            ON CONFLICT(content_id) DO NOTHING;
            """;
        var linked = command.ExecuteNonQuery() == 1;
        if (!linked)
        {
            command.CommandText = """
                SELECT 1 FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
                WHERE f.root_id = $root AND f.series_key = $series AND f.id = $file AND f.content_id = $content AND
                """ + " " + ConfirmedSeries;
            linked = command.ExecuteScalar() is not null;
        }
        transaction.Commit();
        return linked;
    }

    private static SeriesMetadata WriteMetadata(SqliteCommand command, SeriesMetadata metadata, bool attachFiles = true)
    {
        if (metadata.ProviderId is null && ReadMetadata(command) is { ProviderId: not null } existing)
        {
            metadata = metadata with { ProviderId = existing.ProviderId, ProviderDetails = existing.ProviderDetails };
        }

        command.CommandText = "SELECT " + MetadataIdForGroup;
        var id = command.ExecuteScalar() as string ?? Guid.NewGuid().ToString("N");
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", metadata.Title);
        command.Parameters.AddWithValue("$kind", metadata.Kind);
        command.Parameters.AddWithValue("$alternate", (object?)metadata.AlternateTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", (object?)metadata.Summary ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (object?)metadata.Status ?? DBNull.Value);
        command.Parameters.AddWithValue("$chapters", (object?)metadata.TotalChapters ?? DBNull.Value);
        command.Parameters.AddWithValue("$volumes", (object?)metadata.TotalVolumes ?? DBNull.Value);
        command.Parameters.AddWithValue("$provider", (object?)metadata.ProviderId ?? DBNull.Value);
        var providerDetails = metadata.ProviderDetails is { } details ? JsonSerializer.Serialize(details) : null;
        command.Parameters.AddWithValue("$providerDetails", (object?)providerDetails ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("$attachFiles", attachFiles);
        command.CommandText = """
            INSERT INTO series_metadata(id, series_key, title, alternate_title, kind, summary, status, total_chapters, total_volumes,
                provider_id, provider_details, updated_at)
                VALUES ($id, $series, $title, $alternate, $kind, $summary, $status, $chapters, $volumes, $provider, $providerDetails, $now)
            ON CONFLICT(id) DO UPDATE SET title = excluded.title, kind = excluded.kind, alternate_title = excluded.alternate_title,
                summary = excluded.summary, status = excluded.status, total_chapters = excluded.total_chapters,
                total_volumes = excluded.total_volumes, provider_id = excluded.provider_id,
                provider_details = excluded.provider_details, updated_at = excluded.updated_at;
            INSERT INTO series_metadata_binding(root_id, series_key, metadata_id) VALUES ($root, $series, $id)
            ON CONFLICT(root_id, series_key) DO UPDATE SET metadata_id = excluded.metadata_id;
            INSERT INTO content_metadata(content_id, metadata_id)
                SELECT DISTINCT content_id, $id FROM library_file
                WHERE root_id = $root AND series_key = $series AND content_id IS NOT NULL AND $attachFiles
            ON CONFLICT(content_id) DO UPDATE SET metadata_id = excluded.metadata_id
                WHERE EXISTS (SELECT 1 FROM series_metadata previous
                    WHERE previous.id = content_metadata.metadata_id AND previous.series_key = $series);
            """;
        command.ExecuteNonQuery();
        return metadata;
    }

    private static SeriesMetadata? ReadMetadata(SqliteCommand command)
    {
        command.CommandText = """
            SELECT title, kind, alternate_title, summary, status, total_chapters, total_volumes, provider_id, provider_details
            FROM series_metadata WHERE id =
            """ + " " + MetadataIdForGroup;
        using var rows = command.ExecuteReader();
        if (!rows.Read()) return null;

        var details = ReadText(rows, 8) is { } stored ? JsonSerializer.Deserialize<AniListDetails>(stored) : null;
        var providerId = ReadInt(rows, 7);
        var summary = ReadText(rows, 3);
        return new(rows.GetString(0), rows.GetString(1), ReadText(rows, 2),
            providerId.HasValue ? MetadataAgent.PlainDescription(summary) : summary, ReadText(rows, 4),
            ReadInt(rows, 5), ReadInt(rows, 6), providerId, details);
    }

    public bool SaveCoverage(Guid root, string seriesKey, string contentId, string volume, IReadOnlyList<string> chapters, bool wholeVolume)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, seriesKey);
        command.Parameters.AddWithValue("$content", contentId);
        if (!ContentInGroup(command)) return false;

        command.CommandText = """
            SELECT 1 FROM file_operation WHERE content_id = $content AND state IN ('applying', 'restoring') LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null) return false;

        command.Parameters.AddWithValue("$wholeVolume", wholeVolume ? volume : DBNull.Value);
        command.CommandText = """
            DELETE FROM content_chapter WHERE content_id = $content;
            UPDATE library_content SET manual_volume = $wholeVolume WHERE id = $content;
            """;
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO content_chapter(content_id, volume, chapter) VALUES ($content, $volume, $chapter);";
        command.Parameters.AddWithValue("$volume", volume);
        var chapterParameter = command.Parameters.Add("$chapter", SqliteType.Text);
        foreach (var chapter in chapters)
        {
            chapterParameter.Value = chapter;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return true;
    }

    public IReadOnlyList<SeriesChapter>? ReadCoverage(Guid root, string seriesKey, string contentId)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = GroupCommand(database, transaction, root, seriesKey);
        command.Parameters.AddWithValue("$content", contentId);
        if (!ContentInGroup(command)) return null;

        command.CommandText = """
            SELECT volume, chapter, NULL FROM content_chapter WHERE content_id = $content
            UNION ALL
            SELECT inferred.volume, chapter.value, inferred.source
            FROM active_inferred_volume_chapters inferred, json_each(inferred.chapters) chapter
            WHERE inferred.content_id = $content AND NOT EXISTS (SELECT 1 FROM content_chapter c WHERE c.content_id = $content);
            """;
        var result = new List<SeriesChapter>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var volume = rows.GetString(0);
            result.Add(new(volume.Length == 0 ? null : volume, rows.GetString(1), ReadText(rows, 2)));
        }
        return result.OrderBy(chapter => Numeric(chapter.Chapter)).ToArray();
    }

    private static SqliteCommand GroupCommand(SqliteConnection database, SqliteTransaction transaction, Guid root, string seriesKey, bool published = false)
    {
        var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$root", root);
        BindSeriesGroup(command, seriesKey, published);
        return command;
    }

    private static bool GroupExists(SqliteCommand command, bool published = false)
    {
        command.CommandText = """
            SELECT 1 FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            WHERE f.root_id = $root AND f.series_key = $series
            """ + SeriesVisibility(published) + " LIMIT 1;";
        return command.ExecuteScalar() is not null;
    }

    private static bool ContentInGroup(SqliteCommand command, bool published = false)
    {
        command.CommandText = """
            SELECT 1 FROM library_file f JOIN library_folder folder ON folder.root_id = f.root_id
            WHERE f.root_id = $root AND f.content_id = $content AND
            """ + " " + GroupSeriesKeys + SeriesVisibility(published) + " LIMIT 1;";
        return command.ExecuteScalar() is not null;
    }

    private static decimal Numeric(string? value) => decimal.TryParse(value, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
    private static string? ReadText(SqliteDataReader rows, int index) => rows.IsDBNull(index) ? null : rows.GetString(index);
    private static int? ReadInt(SqliteDataReader rows, int index) => rows.IsDBNull(index) ? null : rows.GetInt32(index);

    private static void StageKnownSeries(SqliteConnection database)
    {
        using var command = database.CreateCommand();
        command.CommandText = """
            UPDATE scan_file AS f SET series_key = m.series_key
            FROM scan_hash h JOIN library_content c ON c.sha256 = h.sha256
            JOIN content_metadata cm ON cm.content_id = c.id JOIN series_metadata m ON m.id = cm.metadata_id
            WHERE f.relative_path = h.relative_path;
            """;
        command.ExecuteNonQuery();
    }
}
