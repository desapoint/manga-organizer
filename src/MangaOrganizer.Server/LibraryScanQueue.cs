using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed class LibraryScanQueue(LibraryStore store, ILogger<LibraryScanQueue> logger) : BackgroundService
{
    private sealed record Work(LibraryRoot Root, ScanProgress Progress, bool Resume)
    {
        public CancellationTokenSource Control { get; } = new();
        public bool Running { get; set; }
        public bool Skipped { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, Work> active = new();
    private readonly Channel<Work> requests = Channel.CreateUnbounded<Work>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    public ScanSnapshot? Queue(LibraryRoot root, bool full = false, bool resume = false)
    {
        var previous = resume ? store.ReadPausedScan(root.Id) : null;
        if (resume && previous is null || !store.TryQueue(root.Id, resume)) return null;

        var scan = new ScanProgress(previous?.Full ?? full, previous);
        var work = new Work(root, scan, resume);
        active[root.Id] = work;
        if (requests.Writer.TryWrite(work)) return previous ?? scan.Snapshot();

        active.TryRemove(new KeyValuePair<Guid, Work>(root.Id, work));
        work.Control.Dispose();
        store.SetState(root.Id, resume ? "paused" : "interrupted");
        return null;
    }

    public ScanSnapshot? Status(Guid rootId) => active.TryGetValue(rootId, out var work) ? work.Progress.Snapshot() : null;

    public bool Control(Guid rootId, string action)
    {
        if (!active.TryGetValue(rootId, out var work)) return action == "stop" && store.StopPausedScan(rootId);
        lock (work)
        {
            if (work.Skipped || work.Progress.RequestedAction is not null) return false;
            if (!work.Running)
            {
                if (action != "stop") return false;
                store.SetState(rootId, work.Resume ? "paused" : "stopped");
                if (work.Resume) store.StopPausedScan(rootId);
                work.Skipped = true;
                active.TryRemove(new KeyValuePair<Guid, Work>(rootId, work));
                return true;
            }

            if (work.Progress.Snapshot().Phase == "publishing") return false;
            work.Progress.RequestedAction = action;
            work.Control.Cancel();
            return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in requests.Reader.ReadAllAsync(stoppingToken))
        {
            stoppingToken.ThrowIfCancellationRequested();
            var (root, scan, resume) = work;
            lock (work)
            {
                if (work.Skipped)
                {
                    work.Control.Dispose();
                    continue;
                }
                store.SetState(root.Id, "scanning");
                work.Running = true;
            }

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, work.Control.Token);
            try
            {
                try
                {
                    LibraryScanner.RequireRoot(root.Path);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    RecordFailure(root.Id, resume ? "paused" : "unavailable", error);
                    continue;
                }
                store.Refresh(root, scan, resume, cancellation.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                store.SetState(root.Id, resume ? "paused" : "interrupted");
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
            {
                RecordFailure(root.Id, resume ? "paused" : "incomplete", error);
            }
            finally
            {
                lock (work)
                {
                    work.Skipped = true;
                    active.TryRemove(new KeyValuePair<Guid, Work>(root.Id, work));
                    work.Control.Dispose();
                }
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
