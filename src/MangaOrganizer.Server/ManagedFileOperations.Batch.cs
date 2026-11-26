namespace MangaOrganizer.Server;

internal sealed record BatchPreviewRequest(FileOperationRequest?[]? Items);
internal sealed record BatchApplyRequest(Guid[]? Ids);
internal sealed record BatchPreviewItem(int Index, FileOperationView? Plan, string? Error);
internal sealed record BatchApplyItem(Guid Id, FileOperationView? Result, string? Error);

internal sealed partial class ManagedFileOperations
{
    internal const int MaxBatchItems = 50;

    public IReadOnlyList<BatchPreviewItem> PreviewBatch(IReadOnlyList<FileOperationRequest?> requests)
    {
        var paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var plans = new FileOperation?[requests.Count];
        var results = new BatchPreviewItem[requests.Count];
        var reservations = new Dictionary<string, int>(paths);
        var collisions = new HashSet<int>();

        void Reserve(string path, int index)
        {
            if (!reservations.TryAdd(path, index) && reservations[path] != index)
            {
                collisions.Add(index);
                collisions.Add(reservations[path]);
            }
        }

        for (var index = 0; index < requests.Count; index++)
        {
            try
            {
                var request = requests[index] ?? throw new FileOperationException("Choose an indexed file.");
                var operation = BuildPlan(request);
                plans[index] = operation;
                Reserve(Source(operation).FullName, index);
                Reserve(Target(operation).FullName, index);
                foreach (var cover in operation.Covers) Reserve(Path.Combine(operation.TargetBase, cover.TargetPath), index);
            }
            catch (Exception error) when (error is FileOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                results[index] = new(index, null, error is FileOperationException ? error.Message : "The source or destination could not be verified.");
            }
        }
        for (var index = 0; index < plans.Length; index++)
        {
            if (collisions.Contains(index)) results[index] = new(index, null, "This source or destination conflicts with another selected file.");
            else if (plans[index] is { } operation)
            {
                store.RecordFileOperation(operation);
                results[index] = new(index, View(operation), null);
            }
        }
        return results;
    }

    public async Task<IReadOnlyList<BatchApplyItem>> ApplyBatch(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var items = new List<BatchApplyItem>(ids.Count);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await Run(id, false, cancellationToken);
                items.Add(new(id, result, result is null ? "The plan is no longer available." : null));
            }
            catch (FileOperationException error)
            {
                items.Add(new(id, null, error.Message));
            }
        }
        return items;
    }
}
