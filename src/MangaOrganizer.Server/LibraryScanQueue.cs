using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed class LibraryScanQueue(LibraryStore store, ILogger<LibraryScanQueue> logger) : BackgroundService
{
    private sealed record Work(LibraryRoot Root, ScanProgress Progress);

    private readonly ConcurrentDictionary<Guid, ScanProgress> active = new();
    private readonly Channel<Work> requests = Channel.CreateUnbounded<Work>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    public bool TryQueue(LibraryRoot root)
    {
        if (!store.TryQueue(root.Id)) return false;

        var scan = new ScanProgress();
        active[root.Id] = scan;
        if (requests.Writer.TryWrite(new(root, scan))) return true;

        active.TryRemove(new KeyValuePair<Guid, ScanProgress>(root.Id, scan));
        store.SetState(root.Id, "interrupted");
        return false;
    }

    public ScanSnapshot? Status(Guid rootId) => active.TryGetValue(rootId, out var progress) ? progress.Snapshot() : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in requests.Reader.ReadAllAsync(stoppingToken))
        {
            stoppingToken.ThrowIfCancellationRequested();
            var (root, scan) = work;
            store.SetState(root.Id, "scanning");
            try
            {
                try
                {
                    LibraryScanner.RequireRoot(root.Path);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    RecordFailure(root.Id, "unavailable", error);
                    continue;
                }

                store.Refresh(root, scan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                store.SetState(root.Id, "interrupted");
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
            {
                RecordFailure(root.Id, "incomplete", error);
            }
            finally
            {
                active.TryRemove(new KeyValuePair<Guid, ScanProgress>(root.Id, scan));
            }
        }
    }

    private void RecordFailure(Guid rootId, string state, Exception error)
    {
        logger.LogWarning("Scan for library root {RootId} ended in state {State} ({ErrorType}).",
            rootId, state, error.GetType().Name);
        store.SetState(rootId, state);
    }
}
