using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record DuplicateReviewGroup(string Kind, long Files, string Status, string Fingerprint, long Latest, bool EvidenceChanged);
internal sealed record DuplicateDecisionEntry(long Id, string Kind, string Decision, string? Note, DateTimeOffset DecidedAt);
internal sealed record DuplicateDecisionSummary(IReadOnlyList<DuplicateReviewGroup> Groups, IReadOnlyList<DuplicateDecisionEntry> Recent);

internal sealed partial class LibraryStore
{
    private const string DuplicateReviewStatuses = """
        , review_groups AS MATERIALIZED (
            SELECT f.root_id, f.series_key, r.reason, COUNT(*) AS files,
                (SELECT MAX(d.id) FROM duplicate_review_decision d
                    WHERE d.root_id = f.root_id AND d.series_key = f.series_key AND d.kind = r.reason) AS latest
            FROM reasons r JOIN scoped f ON f.id = r.id
            WHERE $kind IS NULL OR r.reason = $kind
            GROUP BY f.root_id, f.series_key, r.reason
        ), review_evidence AS MATERIALIZED (
            SELECT g.*, d.decision, d.fingerprint AS saved_fingerprint,
                CASE WHEN $snapshot OR d.decision IN ('keep', 'defer') THEN duplicate_review_digest((
                    SELECT json_group_array(json_array(f.id, f.content_id, f.relative_path,
                        root.state IN ('unavailable', 'emptyReview'),
                        CASE WHEN g.reason = 'bytes' THEN (
                            SELECT json_group_array(json_array(peer.root_id, peer.id, peer.relative_path,
                                peer_root.state IN ('unavailable', 'emptyReview')) ORDER BY peer.root_id, peer.id)
                            FROM library_file peer JOIN library_folder folder ON folder.root_id = peer.root_id
                            JOIN library_root peer_root ON peer_root.id = peer.root_id
                            WHERE peer.content_id = f.content_id AND peer.id <> f.id
                        ) ELSE json_array(f.volume_hint, f.chapter_hint, tag.revision, (
                            SELECT json_group_array(json_array(label.volume, label.chapter) ORDER BY label.volume, label.chapter)
                            FROM chapter_labels label WHERE label.id = f.id
                        )) END) ORDER BY f.id)
                    FROM reasons r JOIN scoped f ON f.id = r.id JOIN library_root root ON root.id = f.root_id
                    LEFT JOIN release_tag tag ON tag.content_id = f.content_id
                    WHERE f.root_id = g.root_id AND f.series_key = g.series_key AND r.reason = g.reason
                )) END AS fingerprint
            FROM review_groups g LEFT JOIN duplicate_review_decision d ON d.id = g.latest
        ), review_status AS (
            SELECT *, CASE WHEN saved_fingerprint = fingerprint AND decision = 'keep' THEN 'kept'
                WHEN saved_fingerprint = fingerprint AND decision = 'defer' THEN 'deferred'
                ELSE 'pending' END AS status
            FROM review_evidence
        )
        """;

    public DuplicateDecisionSummary ReadDuplicateDecisions(Guid root, string series)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: true);
        using var command = DuplicateDecisionCommand(database, transaction, root, series, null);
        var groups = ReadDuplicateReviewGroups(command);
        command.CommandText = """
            SELECT id, kind, decision, note, decided_at FROM duplicate_review_decision
            WHERE root_id = $root AND series_key = $series ORDER BY id DESC LIMIT 15;
            """;
        var recent = new List<DuplicateDecisionEntry>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
            recent.Add(new(rows.GetInt64(0), rows.GetString(1), rows.GetString(2), ReadText(rows, 3), rows.GetDateTimeOffset(4)));
        return new(groups, recent);
    }

    public bool SaveDuplicateDecision(Guid root, string series, string kind, string fingerprint, long latest,
        string decision, string? note, Guid owner)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = DuplicateDecisionCommand(database, transaction, root, series, kind);
        var current = ReadDuplicateReviewGroups(command).SingleOrDefault();
        if (current is null || current.Fingerprint != fingerprint || current.Latest != latest
            || decision == "reopen" && current.Status == "pending")
            return false;

        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$decision", decision);
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow);
        command.CommandText = """
            INSERT INTO duplicate_review_decision(root_id, series_key, kind, fingerprint, decision, note, owner_id, decided_at)
            VALUES ($root, $series, $kind, $fingerprint, $decision, $note, $owner, $now);
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    private static IReadOnlyList<DuplicateReviewGroup> ReadDuplicateReviewGroups(SqliteCommand command)
    {
        command.CommandText = DuplicateReasons + DuplicateReviewStatuses + """
            SELECT reason, files, status, fingerprint, COALESCE(latest, 0),
                COALESCE(decision IN ('keep', 'defer') AND saved_fingerprint <> fingerprint, 0)
            FROM review_status ORDER BY reason;
            """;
        var groups = new List<DuplicateReviewGroup>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
            groups.Add(new(rows.GetString(0), rows.GetInt64(1), rows.GetString(2), rows.GetString(3), rows.GetInt64(4), rows.GetBoolean(5)));
        return groups;
    }

    private static SqliteCommand DuplicateDecisionCommand(SqliteConnection database, SqliteTransaction transaction,
        Guid root, string series, string? kind)
    {
        RegisterReviewFingerprint(database);
        var command = DuplicateQueueCommand(database, transaction, root, series, kind, 0, 0);
        command.Parameters.AddWithValue("$snapshot", true);
        return command;
    }

    private static void RegisterReviewFingerprint(SqliteConnection database) =>
        database.CreateFunction<string, string>("duplicate_review_digest",
            payload => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))), isDeterministic: true);
}
