using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed record SeriesFilters(string Search, bool CoverOnly, string[] IncludeTags, string[] ExcludeTags)
{
    public static SeriesFilters Empty { get; } = new("", false, [], []);

    public static SeriesFilters? FromInput(string? search, bool coverOnly, IEnumerable<string?> include, IEnumerable<string?> exclude)
    {
        if (search is { Length: > 160 }) return null;
        var included = Normalize(include);
        var excluded = Normalize(exclude);
        return included is null || excluded is null || included.Intersect(excluded, StringComparer.OrdinalIgnoreCase).Any()
            ? null : new(search?.Trim() ?? "", coverOnly, included, excluded);
    }

    private static string[]? Normalize(IEnumerable<string?> values)
    {
        var list = values.Take(21).ToArray();
        return list.Length > 20 || list.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 100)
            ? null : list.Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal sealed record SeriesCollection(Guid Id, string Name, SeriesFilters? Filters, bool ContainsSeries);

internal sealed partial class LibraryStore
{
    public IReadOnlyList<SeriesCollection> ReadCollections(Guid? root = null, string? series = null, Guid? id = null)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.name, c.filters, EXISTS(SELECT 1 FROM series_collection_item i
                WHERE i.collection_id = c.id AND i.root_id = $root AND i.series_key = $series)
            FROM series_collection c WHERE $id IS NULL OR c.id = $id ORDER BY c.name COLLATE NOCASE, c.id;
            """;
        command.Parameters.AddWithValue("$id", (object?)id ?? DBNull.Value);
        command.Parameters.AddWithValue("$root", (object?)root ?? DBNull.Value);
        command.Parameters.AddWithValue("$series", (object?)series ?? DBNull.Value);
        using var rows = command.ExecuteReader();
        var collections = new List<SeriesCollection>();
        while (rows.Read())
        {
            collections.Add(new(rows.GetGuid(0), rows.GetString(1),
                ReadText(rows, 2) is { } filters ? JsonSerializer.Deserialize<SeriesFilters>(filters) : null, rows.GetBoolean(3)));
        }
        return collections;
    }

    public SeriesCollection? SaveCollection(Guid? id, string name, SeriesFilters? filters)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = id.HasValue
            ? "UPDATE series_collection SET name = $name, filters = $filters WHERE id = $id AND (filters IS NULL) = ($filters IS NULL);"
            : "INSERT INTO series_collection(id, name, filters) VALUES ($id, $name, $filters);";
        var collectionId = id ?? Guid.NewGuid();
        command.Parameters.AddWithValue("$id", collectionId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$filters", filters is null ? DBNull.Value : JsonSerializer.Serialize(filters));
        return command.ExecuteNonQuery() == 1 ? new(collectionId, name, filters, false) : null;
    }

    public bool RemoveCollection(Guid id)
    {
        using var database = OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "DELETE FROM series_collection WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() == 1;
    }

    public bool SetCollectionMembership(Guid id, Guid root, string series, bool included)
    {
        using var database = OpenDatabase();
        using var transaction = database.BeginTransaction(deferred: false);
        using var command = GroupCommand(database, transaction, root, series);
        command.Parameters.AddWithValue("$id", id);
        command.CommandText = "SELECT 1 FROM series_collection WHERE id = $id AND filters IS NULL;";
        if (command.ExecuteScalar() is null || included && !GroupExists(command)) return false;
        command.CommandText = included
            ? "INSERT OR IGNORE INTO series_collection_item VALUES ($id, $root, $series);"
            : "DELETE FROM series_collection_item WHERE collection_id = $id AND root_id = $root AND series_key = $series;";
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }
}
