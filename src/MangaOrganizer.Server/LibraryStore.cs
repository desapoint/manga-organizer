using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record LibraryRoot(Guid Id, string Path);
internal sealed record LibraryRootStatus(Guid Id, string Path, string State, long FileCount,
    DateTimeOffset? LastAttemptAt, DateTimeOffset? LastScannedAt, long SkippedLinks, string? Name, string Kind, bool HasCovers,
    bool Managed, ScanSnapshot? ScanProgress = null, Guid? IntakeTargetId = null);
internal sealed record CatalogEntry(string Id, string RelativePath, string Extension, long Size, DateTimeOffset ModifiedAt,
    string? ContentId, string? Sha256, long? IndexedLocations, string? Volume, string? Chapter, bool HasCover,
    string? SeriesCoverKey, string? SeriesTitle, bool HasSeriesMetadata, string SeriesKey, bool AssociationConfirmed, bool HasSeriesSuggestion,
    DoujinFilename? FilenameEvidence, string? IntakePath);
internal sealed record CatalogPage<T>(long Total, IReadOnlyList<T> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool LookupQueued = false);

internal sealed partial class LibraryStore
{
    private readonly string dataDirectory;

    public LibraryStore(string dataDirectory, Func<IReadOnlyList<string>> initialPaths)
    {
        this.dataDirectory = dataDirectory;
        using var database = OpenCoverDatabase();
        CoverCache.Initialize(database);
        using var transaction = database.BeginTransaction();
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE library_root SET state = 'interrupted' WHERE state IN ('queued', 'scanning');
            UPDATE file_operation SET state = 'review', message = 'The server stopped during this operation. Verify retained files before recovery.'
                WHERE state IN ('applying', 'restoring');
            """;
        command.ExecuteNonQuery();

        command.CommandText = "INSERT OR IGNORE INTO library_settings(singleton) VALUES (1);";
        if (command.ExecuteNonQuery() == 1)
        {
            foreach (var path in initialPaths())
            {
                SaveFolder(command, null, path, null, "library");
            }
        }
        transaction.Commit();
    }

    public bool TryGetRoot(Guid id, out LibraryRoot? root)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT path FROM library_folder WHERE root_id = $id;";
        command.Parameters.AddWithValue("$id", id);
        root = command.ExecuteScalar() is string path ? new LibraryRoot(id, path) : null;
        return root is not null;
    }

    public IReadOnlyList<LibraryRootStatus> ReadRoots()
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT r.id, f.path, r.state, r.file_count, r.last_attempt_at,
                r.last_scanned_at, r.skipped_links, f.name, f.kind,
                EXISTS(SELECT 1 FROM library_series_cover cover WHERE cover.root_id = r.id) OR EXISTS(
                    SELECT 1 FROM library_file file
                    LEFT JOIN content_metadata cm ON cm.content_id = file.content_id
                    LEFT JOIN series_metadata m ON m.id = cm.metadata_id AND
            """ + " " + LinkedSeriesScope("file", "m") + """

                    JOIN series_cover_preference pref ON pref.series_id = m.id
                    WHERE file.root_id = r.id
                ), f.managed, route.target_root
            FROM library_folder f JOIN library_root r ON r.id = f.root_id
            LEFT JOIN library_intake_target route ON route.source_root = f.root_id
            ORDER BY f.rowid;
            """;
        var result = new List<LibraryRootStatus>();
        using var row = command.ExecuteReader();
        while (row.Read())
        {
            result.Add(new(row.GetGuid(0), row.GetString(1), row.GetString(2), row.GetInt64(3),
                row.IsDBNull(4) ? null : row.GetDateTimeOffset(4),
                row.IsDBNull(5) ? null : row.GetDateTimeOffset(5), row.GetInt64(6),
                row.IsDBNull(7) ? null : row.GetString(7), row.GetString(8), row.GetBoolean(9), row.GetBoolean(10),
                IntakeTargetId: row.IsDBNull(11) ? null : row.GetGuid(11)));
        }
        return result;
    }

    public CatalogPage<CatalogEntry> ReadFiles(Guid id, int offset, int limit, string? seriesKey, string search, string? volume, string? view)
    {
        using var database = OpenCoverDatabase();
        RegisterTitleSearch(database);
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$root", id);
        command.Parameters.AddWithValue("$search", search);
        if (seriesKey is not null)
        {
            BindSeriesGroup(command, seriesKey, view == "library");
        }
        var scope = " WHERE f.root_id = $root" + (seriesKey is null ? "" : " AND " + GroupSeriesKeys) + CatalogVisibility(view);
        var volumeCoverage = "";
        if (volume is not null && seriesKey is not null)
        {
            AddWorkKey(command, id);
            command.Parameters.AddWithValue("$volume", volume);
            volumeCoverage = "WITH covered AS (" + ScopedGroupCoverage(view == "library") + ") ";
            scope += " AND " + GroupVolumeMatch;
        }
        var sources = """
            FROM library_file_numbered f LEFT JOIN library_content c ON c.id = f.content_id
            LEFT JOIN series_metadata_binding binding ON binding.root_id = f.root_id AND binding.series_key = f.series_key
            LEFT JOIN content_metadata cm ON cm.content_id = f.content_id
            LEFT JOIN series_metadata linked ON linked.id = cm.metadata_id AND
            """ + " " + LinkedSeriesScope("f", "linked") + """

            LEFT JOIN series_metadata m ON m.id = COALESCE(linked.id, binding.metadata_id)
            """;
        const string searchFilter = """
             AND ($search = '' OR title_contains(f.relative_path, $search) OR title_contains(COALESCE(f.series_title, ''), $search)
                OR title_contains(COALESCE(m.title, ''), $search) OR title_contains(COALESCE(m.alternate_title, ''), $search))
            """;
        if (search.Length > 0) scope += " " + searchFilter;
        command.CommandText = volumeCoverage + "SELECT COUNT(*) " + sources + scope;
        var total = (long)command.ExecuteScalar()!;
        command.CommandText = volumeCoverage + """
            SELECT f.id, f.relative_path, f.extension, f.size, f.modified_at, c.id, lower(hex(c.sha256)),
                CASE WHEN c.id IS NOT NULL THEN (
                    SELECT COUNT(*) FROM library_file location
                    JOIN library_folder folder ON folder.root_id = location.root_id
                    WHERE location.content_id = c.id
                ) END, f.volume_hint, f.chapter_hint,
                EXISTS(SELECT 1 FROM artwork.archive_cover cover WHERE cover.archive_sha = c.sha256),
                (SELECT cover.series_key FROM library_series_cover cover WHERE cover.root_id = f.root_id AND cover.series_key = f.series_key),
                COALESCE(m.title, f.series_title), NOT (
            """ + " " + MissingMetadata + "), f.series_key, " + ConfirmedSeries + ", f.series_title IS NOT NULL, " + IntakeNumbered
            + ", " + FinishedOneShot + """
            , json_extract(m.provider_details, '$.StartYear'),
                (SELECT inferred.volume FROM active_inferred_volume_chapters inferred WHERE inferred.content_id = f.content_id),
                (SELECT json_group_array(json_array(chapter.volume, chapter.chapter) ORDER BY CAST(chapter.chapter AS REAL), chapter.chapter)
                    FROM content_chapter chapter WHERE chapter.content_id = f.content_id)
            """
            + sources + scope + " ORDER BY f.relative_path LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<CatalogEntry>(limit);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var verified = !rows.IsDBNull(5);
            var path = rows.GetString(1);
            var title = ReadText(rows, 12);
            var confirmed = rows.GetBoolean(15);
            var assigned = JsonSerializer.Deserialize<string[][]>(rows.GetString(21)) ?? [];
            var volumeHint = ReadText(rows, 8);
            var chapterHint = ReadText(rows, 9);
            string? intakePath = null;
            if (view != "library" && confirmed && rows.GetBoolean(17) && title is not null)
            {
                intakePath = IntakeNaming.PathFor(path, title, ReadInt(rows, 19), volumeHint ?? ReadText(rows, 20),
                    chapterHint, assigned, rows.GetBoolean(18));
            }

            // The stored assignment takes precedence over the scanned filename.
            if (view == "library" && assigned.Length != 0)
            {
                var assignedVolumes = assigned.Select(label => label[0]).Distinct().ToArray();
                volumeHint = assignedVolumes is [var assignedVolume] && assignedVolume.Length > 0 ? assignedVolume : null;
                chapterHint = assigned is [var only] ? only[1] : null;
            }
            items.Add(new(rows.GetString(0), path, rows.GetString(2), rows.GetInt64(3), rows.GetDateTimeOffset(4),
                verified ? rows.GetString(5) : null, verified ? rows.GetString(6) : null, verified ? rows.GetInt64(7) : null,
                volumeHint, chapterHint, rows.GetBoolean(10), ReadText(rows, 11), title,
                rows.GetBoolean(13), rows.GetString(14), confirmed, rows.GetBoolean(16),
                view == "library" ? null : DoujinFilename.Parse(Path.GetFileNameWithoutExtension(path)), intakePath));
        }
        return new(total, items);
    }

    public bool TryQueue(Guid id)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            UPDATE library_root SET state = 'queued', last_attempt_at = $now
            WHERE id = $id AND state NOT IN ('queued', 'scanning')
                AND EXISTS (SELECT 1 FROM library_folder WHERE root_id = $id)
                AND NOT EXISTS (SELECT 1 FROM file_operation op WHERE op.state IN ('applying', 'restoring')
                    AND ($id IN (op.source_root, op.target_root) OR op.state = 'applying' AND $id = op.quarantine_keep_root));
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        return command.ExecuteNonQuery() == 1;
    }

    public void SetState(Guid id, string state)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "UPDATE library_root SET state = $state WHERE id = $id;";
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Refresh(LibraryRoot root, ScanProgress scan, CancellationToken cancellationToken)
    {
        using var database = OpenCoverDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            CREATE TEMP TABLE scan_file (
                relative_path TEXT PRIMARY KEY,
                extension TEXT NOT NULL,
                size INTEGER NOT NULL,
                modified_at TEXT NOT NULL,
                series_key TEXT NOT NULL,
                series_title TEXT,
                directory TEXT NOT NULL,
                volume_hint TEXT,
                chapter_hint TEXT,
                creator_series_key TEXT,
                creator_series_title TEXT,
                creator_chapter TEXT
            );
            CREATE TEMP TABLE scan_hash (
                relative_path TEXT PRIMARY KEY,
                sha256 BLOB,
                detected_chapters TEXT,
                chapter_source TEXT
            );
            CREATE TEMP TABLE scan_cover (
                series_key TEXT PRIMARY KEY,
                relative_path TEXT NOT NULL,
                size INTEGER NOT NULL,
                modified_at TEXT NOT NULL,
                archive_entry TEXT,
                sha256 BLOB NOT NULL
            );
            CREATE TEMP TABLE scan_sidecar (
                series_key TEXT NOT NULL,
                relative_path TEXT PRIMARY KEY,
                image_sha BLOB NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        scan.StartPhase("discovering");
        var (count, skippedLinks) = StageFiles(database, root.Path, scan, cancellationToken);
        if (count > 0)
        {
            StageHashes(database, root.Path, scan, cancellationToken);
            StageKnownSeries(database, root.Id);
            StageIntakeSplits(database, root.Id);
            StageCovers(database, root.Path, scan, cancellationToken);
        }
        LibraryScanner.RequireRoot(root.Path);
        cancellationToken.ThrowIfCancellationRequested();
        scan.StartPhase("publishing");

        using var transaction = database.BeginTransaction();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", root.Id);
        command.CommandText = "SELECT file_count FROM library_root WHERE id = $id;";
        var previousCount = (long)command.ExecuteScalar()!;
        if (count == 0 && previousCount > 0)
        {
            command.CommandText = "UPDATE library_root SET state = 'emptyReview' WHERE id = $id;";
        }
        else
        {
            QueueArrivingSeries(command);
            command.CommandText = """
                INSERT INTO library_content(sha256)
                    SELECT DISTINCT sha256 FROM scan_hash WHERE sha256 IS NOT NULL
                    ON CONFLICT(sha256) DO NOTHING;
                INSERT INTO inferred_volume_chapters(content_id, volume, chapters, source)
                    SELECT c.id, MIN(s.volume_hint), MIN(h.detected_chapters), MIN(h.chapter_source)
                    FROM scan_hash h JOIN scan_file s ON s.relative_path = h.relative_path
                    JOIN library_content c ON c.sha256 = h.sha256
                    WHERE h.detected_chapters IS NOT NULL
                    GROUP BY c.id
                    HAVING COUNT(DISTINCT s.volume_hint) = 1 AND COUNT(DISTINCT h.detected_chapters) = 1
                    ON CONFLICT(content_id) DO NOTHING;
                INSERT INTO library_file(root_id, relative_path, extension, size, modified_at, content_id, series_key, series_title, volume_hint, chapter_hint)
                    SELECT $id, s.relative_path, s.extension, s.size, s.modified_at, c.id, s.series_key, s.series_title, s.volume_hint, s.chapter_hint
                    FROM scan_file s LEFT JOIN scan_hash h ON h.relative_path = s.relative_path
                    LEFT JOIN library_content c ON c.sha256 = h.sha256 WHERE 1
                    ON CONFLICT(root_id, relative_path) DO UPDATE SET
                        extension = excluded.extension, size = excluded.size, modified_at = excluded.modified_at,
                        content_id = excluded.content_id, series_key = excluded.series_key, series_title = excluded.series_title,
                        volume_hint = excluded.volume_hint, chapter_hint = excluded.chapter_hint;
                DELETE FROM import_file_split WHERE EXISTS (
                    SELECT 1 FROM library_file f WHERE f.id = import_file_split.file_id AND f.root_id = $id
                        AND f.content_id IS NOT import_file_split.content_id);
                DELETE FROM library_file WHERE root_id = $id AND relative_path NOT IN (SELECT relative_path FROM scan_file);
                DELETE FROM library_series_cover WHERE root_id = $id;
                INSERT INTO library_series_cover(root_id, series_key, relative_path, size, modified_at, archive_entry, sha256)
                    SELECT $id, series_key, relative_path, size, modified_at, archive_entry, sha256 FROM scan_cover;
                DELETE FROM artwork.folder_cover WHERE root_id = $id OR root_id NOT IN (SELECT root_id FROM library_folder);
                INSERT INTO artwork.folder_cover(root_id, series_key, relative_path, image_sha)
                    SELECT $id, series_key, relative_path, image_sha FROM scan_sidecar;
                DELETE FROM artwork.archive_cover AS cover WHERE NOT EXISTS (
                    SELECT 1 FROM library_content c JOIN library_file f ON f.content_id = c.id JOIN library_folder folder ON folder.root_id = f.root_id
                    WHERE c.sha256 = cover.archive_sha
                );
                DELETE FROM artwork.image WHERE sha256 NOT IN (
                    SELECT image_sha FROM artwork.archive_cover
                    UNION SELECT image_sha FROM artwork.folder_cover
                    UNION SELECT cover.sha256 FROM library_series_cover cover JOIN library_folder folder ON folder.root_id = cover.root_id
                );
                PRAGMA artwork.incremental_vacuum(128);
                UPDATE library_root SET state = 'complete', file_count = $count,
                    last_scanned_at = $now, skipped_links = $skipped WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$count", count);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
            command.Parameters.AddWithValue("$skipped", skippedLinks);
        }
        command.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private static (long Count, long SkippedLinks) StageFiles(SqliteConnection database, string path, ScanProgress scan,
        CancellationToken cancellationToken)
    {
        // Build the inventory in a temporary table without holding the published catalog's write lock.
        using var transaction = database.BeginTransaction(deferred: true);
        using var insert = database.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO scan_file VALUES ($path, $extension, $size, $modified, $seriesKey, $seriesTitle, $directory, $volume, $chapter, $creatorKey, $creatorTitle, $creatorChapter);";
        var relativePath = insert.Parameters.Add("$path", SqliteType.Text);
        var extension = insert.Parameters.Add("$extension", SqliteType.Text);
        var size = insert.Parameters.Add("$size", SqliteType.Integer);
        var modified = insert.Parameters.Add("$modified", SqliteType.Text);
        var seriesKey = insert.Parameters.Add("$seriesKey", SqliteType.Text);
        var seriesTitle = insert.Parameters.Add("$seriesTitle", SqliteType.Text);
        var directory = insert.Parameters.Add("$directory", SqliteType.Text);
        var volume = insert.Parameters.Add("$volume", SqliteType.Text);
        var chapter = insert.Parameters.Add("$chapter", SqliteType.Text);
        var creatorKey = insert.Parameters.Add("$creatorKey", SqliteType.Text);
        var creatorTitle = insert.Parameters.Add("$creatorTitle", SqliteType.Text);
        var creatorChapter = insert.Parameters.Add("$creatorChapter", SqliteType.Text);
        insert.Prepare();

        long count = 0;
        var skippedLinks = LibraryScanner.Collect(path, file =>
        {
            relativePath.Value = file.RelativePath;
            extension.Value = file.Extension;
            size.Value = file.Size;
            modified.Value = file.ModifiedAt;
            var title = SeriesSuggestions.CandidateTitle(file.RelativePath, path);
            seriesKey.Value = SeriesSuggestions.Key(file.RelativePath, title);
            seriesTitle.Value = (object?)title ?? DBNull.Value;
            directory.Value = file.RelativePath[..Math.Max(file.RelativePath.LastIndexOf('/'), 0)];
            var hints = BookNumbers.FromPath(file.RelativePath);
            volume.Value = (object?)hints.Volume ?? DBNull.Value;
            chapter.Value = (object?)hints.Chapter ?? DBNull.Value;
            var creatorRelease = hints.Volume is null && hints.Chapter is null
                ? SeriesSuggestions.NumberedCreatorRelease(file.RelativePath) : null;
            creatorKey.Value = (object?)creatorRelease?.Key ?? DBNull.Value;
            creatorTitle.Value = (object?)creatorRelease?.Title ?? DBNull.Value;
            creatorChapter.Value = (object?)creatorRelease?.Chapter ?? DBNull.Value;
            insert.ExecuteNonQuery();
            count++;
            scan.Discovered(file.Size);
        }, cancellationToken);
        insert.CommandText = """
            WITH numbered AS (
                SELECT creator_series_key, MIN(creator_series_title) AS title FROM scan_file
                WHERE creator_series_key IS NOT NULL
                GROUP BY creator_series_key HAVING COUNT(DISTINCT creator_chapter) >= 2
            )
            UPDATE scan_file AS f SET series_key = numbered.creator_series_key, series_title = numbered.title,
                chapter_hint = f.creator_chapter
            FROM numbered WHERE f.creator_series_key = numbered.creator_series_key;
            """;
        insert.ExecuteNonQuery();
        transaction.Commit();
        return (count, skippedLinks);
    }

    private SqliteConnection OpenDatabase()
    {
        var database = ServerStore.CreateConnection(dataDirectory);
        try
        {
            database.Open();
            return database;
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }
}
