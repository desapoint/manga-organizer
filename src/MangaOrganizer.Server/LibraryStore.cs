using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record LibraryRoot(Guid Id, string Path);
internal sealed record LibraryRootStatus(Guid Id, string Path, string State, long FileCount,
    DateTimeOffset? LastAttemptAt, DateTimeOffset? LastScannedAt, long SkippedLinks, string? Name, string Kind, bool HasCovers,
    bool Managed, ScanSnapshot? ScanProgress = null, Guid? IntakeTargetId = null, ScanSnapshot? LastScan = null,
    ScanLookupProgress? MetadataProgress = null);
internal sealed record ScanLookupProgress(long Total, long Completed);
internal sealed record CatalogEntry(string Id, string RelativePath, string Extension, long Size, DateTimeOffset ModifiedAt,
    string? ContentId, string? Sha256, long? IndexedLocations, string? Volume, string? Chapter, bool HasCover,
    string? SeriesCoverKey, string? SeriesTitle, bool HasSeriesMetadata, string SeriesKey, bool AssociationConfirmed, bool HasSeriesSuggestion,
    DoujinFilename? FilenameEvidence, string? IntakePath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LookupTitle = null);
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
            UPDATE library_root SET state = CASE WHEN EXISTS (
                SELECT 1 FROM library_scan_checkpoint checkpoint WHERE checkpoint.root_id = library_root.id
                    AND checkpoint.file_plan IS NOT NULL) THEN 'paused' ELSE 'interrupted' END
            WHERE state IN ('queued', 'scanning');
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
                ), f.managed, route.target_root, r.last_scan, checkpoint.snapshot,
                (SELECT COUNT(*) FROM library_scan_metadata jobs WHERE jobs.root_id = r.id),
                (SELECT COUNT(*) FROM library_scan_metadata jobs LEFT JOIN metadata_suggestion s ON s.series_key = jobs.series_key
                    WHERE jobs.root_id = r.id AND (COALESCE(s.queued, 0) = 0 OR NOT EXISTS (
                        SELECT 1 FROM library_file candidate WHERE candidate.root_id = jobs.root_id
                            AND candidate.series_key = jobs.series_key AND candidate.content_id IS NOT NULL
                            AND NOT EXISTS (SELECT 1 FROM series_metadata_binding binding WHERE binding.root_id = jobs.root_id
                                AND binding.series_key = jobs.series_key)
                        GROUP BY candidate.series_key HAVING COUNT(DISTINCT (
                            SELECT link.metadata_id FROM content_metadata link WHERE link.content_id = candidate.content_id)) = 0)))
            FROM library_folder f JOIN library_root r ON r.id = f.root_id
            LEFT JOIN library_intake_target route ON route.source_root = f.root_id
            LEFT JOIN library_scan_checkpoint checkpoint ON checkpoint.root_id = r.id AND r.state IN ('paused', 'stopped')
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
                ScanProgress: row.IsDBNull(13) ? null : JsonSerializer.Deserialize<ScanSnapshot>(row.GetString(13)),
                IntakeTargetId: row.IsDBNull(11) ? null : row.GetGuid(11),
                LastScan: row.IsDBNull(12) ? null : JsonSerializer.Deserialize<ScanSnapshot>(row.GetString(12)),
                MetadataProgress: row.GetInt64(14) == 0 ? null : new(row.GetInt64(14), row.GetInt64(15))));
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
        var searchFilter = $"""
             AND ($search = '' OR title_contains(f.relative_path, $search) OR title_contains(COALESCE(f.series_title, ''), $search)
                OR title_contains(COALESCE(m.title, ''), $search) OR {AlternateTitleSearch("m")})
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
            var hasMetadata = rows.GetBoolean(13);
            var lookupTitle = view != "library" && !hasMetadata ? DoujinFilename.LookupOverride(title) : null;
            items.Add(new(rows.GetString(0), path, rows.GetString(2), rows.GetInt64(3), rows.GetDateTimeOffset(4),
                verified ? rows.GetString(5) : null, verified ? rows.GetString(6) : null, verified ? rows.GetInt64(7) : null,
                volumeHint, chapterHint, rows.GetBoolean(10), ReadText(rows, 11), title,
                hasMetadata, rows.GetString(14), confirmed, rows.GetBoolean(16),
                view == "library" ? null : DoujinFilename.Parse(Path.GetFileNameWithoutExtension(path)), intakePath, lookupTitle));
        }
        return new(total, items);
    }

    public bool TryQueue(Guid id, bool resume = false)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            UPDATE library_root SET state = 'queued', last_attempt_at = $now
            WHERE id = $id AND (($resume AND state = 'paused') OR (NOT $resume AND state NOT IN ('queued', 'scanning', 'paused')))
                AND EXISTS (SELECT 1 FROM library_folder WHERE root_id = $id)
                AND NOT EXISTS (SELECT 1 FROM file_operation op WHERE op.state IN ('applying', 'restoring')
                    AND ($id IN (op.source_root, op.target_root) OR op.state = 'applying' AND $id = op.quarantine_keep_root));
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$resume", resume);
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

    public void Refresh(LibraryRoot root, ScanProgress scan, bool resume, CancellationToken cancellationToken)
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
                chapter_source TEXT,
                stamp TEXT
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
                image_sha BLOB NOT NULL,
                stamp TEXT
            );
            """;
        command.ExecuteNonQuery();

        long? completedDiscovery = null;
        try
        {
            scan.StartPhase("discovering");
            var plan = resume ? ReadScanPlan(database, root.Id) : null;
            var (count, skippedLinks) = StageFiles(database, root.Path, scan, plan, cancellationToken);
            completedDiscovery = skippedLinks;
            if (count > 0)
            {
                StageHashes(database, root, scan, resume, cancellationToken);
                StageKnownSeries(database, root.Id);
                StageIntakeSplits(database, root.Id);
            }
            do
            {
                if (count > 0)
                {
                    using var artwork = OpenCoverDatabase();
                    StageCovers(database, artwork, root, scan, cancellationToken);
                }
                LibraryScanner.RequireRoot(root.Path);
                cancellationToken.ThrowIfCancellationRequested();
                scan.StartPhase("publishing");
            } while (!PublishScan(database, root.Id, count, skippedLinks, scan, cancellationToken));
        }
        catch (OperationCanceledException) when (scan.RequestedAction is { } action)
        {
            SaveScanCheckpoint(database, root.Id, scan.Snapshot() with { RequestedAction = null },
                action == "pause" ? "paused" : "stopped", completedDiscovery);
        }
    }

    private static bool PublishScan(SqliteConnection database, Guid rootId, long count, long skippedLinks, ScanProgress scan,
        CancellationToken cancellationToken)
    {
        using var transaction = database.BeginTransaction();
        // A confirmation can change the series key while artwork is being extracted. Reuse the artwork with the updated grouping.
        if (count > 0 && StageKnownSeries(database, rootId, transaction) != 0)
        {
            transaction.Commit();
            return false;
        }

        using var command = database.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", rootId);
        command.CommandText = "SELECT file_count FROM library_root WHERE id = $id;";
        var previousCount = (long)command.ExecuteScalar()!;
        var published = count != 0 || previousCount == 0;
        if (!published)
        {
            command.CommandText = "UPDATE library_root SET state = 'emptyReview' WHERE id = $id;";
        }
        else
        {
            QueueArrivingSeries(command);
            StageRenamedReviews(command);
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
                INSERT INTO library_file(root_id, relative_path, extension, size, modified_at, content_id, series_key,
                    series_title, volume_hint, chapter_hint, scan_stamp, scan_chapters, scan_chapter_source)
                    SELECT $id, s.relative_path, s.extension, s.size, s.modified_at, c.id, s.series_key, s.series_title,
                        s.volume_hint, s.chapter_hint, h.stamp, h.detected_chapters, h.chapter_source
                    FROM scan_file s LEFT JOIN scan_hash h ON h.relative_path = s.relative_path
                    LEFT JOIN library_content c ON c.sha256 = h.sha256 WHERE 1
                    ON CONFLICT(root_id, relative_path) DO UPDATE SET
                        extension = excluded.extension, size = excluded.size, modified_at = excluded.modified_at,
                        content_id = excluded.content_id, series_key = excluded.series_key, series_title = excluded.series_title,
                        volume_hint = excluded.volume_hint, chapter_hint = excluded.chapter_hint, scan_stamp = excluded.scan_stamp,
                        scan_chapters = excluded.scan_chapters, scan_chapter_source = excluded.scan_chapter_source
                    WHERE (library_file.extension, library_file.size, library_file.modified_at, library_file.content_id,
                           library_file.series_key, library_file.series_title, library_file.volume_hint, library_file.chapter_hint,
                           library_file.scan_stamp, library_file.scan_chapters, library_file.scan_chapter_source)
                        IS NOT (excluded.extension, excluded.size, excluded.modified_at, excluded.content_id, excluded.series_key,
                                excluded.series_title, excluded.volume_hint, excluded.chapter_hint, excluded.scan_stamp,
                                excluded.scan_chapters, excluded.scan_chapter_source);
                INSERT OR IGNORE INTO file_association_review(file_id, content_id)
                SELECT f.id, f.content_id FROM scan_renamed_review moved JOIN library_file f ON f.root_id = $id
                    AND f.relative_path = moved.relative_path AND f.content_id = moved.content_id;
                DELETE FROM import_file_split WHERE EXISTS (
                    SELECT 1 FROM library_file f WHERE f.id = import_file_split.file_id AND f.root_id = $id
                        AND f.content_id IS NOT import_file_split.content_id);
                DELETE FROM library_file WHERE root_id = $id AND relative_path NOT IN (SELECT relative_path FROM scan_file);
                DELETE FROM library_series_cover WHERE root_id = $id AND series_key NOT IN (SELECT series_key FROM scan_cover);
                INSERT INTO library_series_cover(root_id, series_key, relative_path, size, modified_at, archive_entry, sha256)
                    SELECT $id, series_key, relative_path, size, modified_at, archive_entry, sha256 FROM scan_cover WHERE 1
                    ON CONFLICT(root_id, series_key) DO UPDATE SET relative_path = excluded.relative_path, size = excluded.size,
                        modified_at = excluded.modified_at, archive_entry = excluded.archive_entry, sha256 = excluded.sha256
                    WHERE (library_series_cover.relative_path, library_series_cover.size, library_series_cover.modified_at,
                           library_series_cover.archive_entry, library_series_cover.sha256)
                        IS NOT (excluded.relative_path, excluded.size, excluded.modified_at, excluded.archive_entry, excluded.sha256);
                DELETE FROM artwork.folder_cover WHERE (root_id = $id AND relative_path NOT IN (SELECT relative_path FROM scan_sidecar))
                    OR root_id NOT IN (SELECT root_id FROM library_folder);
                INSERT INTO artwork.folder_cover(root_id, series_key, relative_path, image_sha)
                    SELECT $id, series_key, relative_path, image_sha FROM scan_sidecar WHERE 1
                    ON CONFLICT(root_id, relative_path) DO UPDATE SET series_key = excluded.series_key, image_sha = excluded.image_sha
                    WHERE (folder_cover.series_key, folder_cover.image_sha) IS NOT (excluded.series_key, excluded.image_sha);
                DELETE FROM library_sidecar_stamp WHERE (root_id = $id AND relative_path NOT IN (
                    SELECT relative_path FROM scan_sidecar WHERE stamp IS NOT NULL)) OR root_id NOT IN (SELECT root_id FROM library_folder);
                INSERT INTO library_sidecar_stamp(root_id, relative_path, stamp)
                    SELECT $id, relative_path, stamp FROM scan_sidecar WHERE stamp IS NOT NULL
                    ON CONFLICT(root_id, relative_path) DO UPDATE SET stamp = excluded.stamp WHERE library_sidecar_stamp.stamp <> excluded.stamp;
                DELETE FROM artwork.archive_cover AS cover WHERE NOT EXISTS (
                    SELECT 1 FROM library_content c JOIN library_file f ON f.content_id = c.id JOIN library_folder folder ON folder.root_id = f.root_id
                    WHERE c.sha256 = cover.archive_sha
                );
                DELETE FROM artwork.archive_without_pages AS miss WHERE NOT EXISTS (
                    SELECT 1 FROM library_content c JOIN library_file f ON f.content_id = c.id JOIN library_folder folder ON folder.root_id = f.root_id
                    WHERE c.sha256 = miss.archive_sha
                );
                DELETE FROM artwork.image WHERE sha256 NOT IN (
                    SELECT image_sha FROM artwork.archive_cover
                    UNION SELECT image_sha FROM artwork.folder_cover
                    UNION SELECT cover.sha256 FROM library_series_cover cover JOIN library_folder folder ON folder.root_id = cover.root_id
                );
                PRAGMA artwork.incremental_vacuum(128);
                """;
        }
        command.ExecuteNonQuery();
        if (published)
        {
            command.CommandText = """
                UPDATE library_root SET state = 'complete', file_count = $count, last_scanned_at = $now,
                    skipped_links = $skipped, last_scan = $scan WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$count", count);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
            command.Parameters.AddWithValue("$skipped", skippedLinks);
            command.Parameters.AddWithValue("$scan", JsonSerializer.Serialize(scan.Snapshot() with { Phase = "complete" }));
            command.ExecuteNonQuery();
        }
        command.CommandText = "DELETE FROM library_scan_checkpoint WHERE root_id = $id;";
        command.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return true;
    }

    private static (long Count, long SkippedLinks) StageFiles(SqliteConnection database, string path, ScanProgress scan,
        (long SkippedLinks, LibraryFileFact[] Files)? savedPlan, CancellationToken cancellationToken)
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
        void AddFile(LibraryFileFact file)
        {
            scan.BeginFile(file.RelativePath);
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
        }

        long skippedLinks;
        if (savedPlan is { } plan)
        {
            skippedLinks = plan.SkippedLinks;
            foreach (var planned in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (MediaFiles.Resolve(path, planned.RelativePath) is { Exists: true } file &&
                    (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0)
                    AddFile(planned with { Size = file.Length, ModifiedAt = file.LastWriteTimeUtc });
            }
        }
        else
        {
            skippedLinks = LibraryScanner.Collect(path, AddFile, cancellationToken);
        }
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
