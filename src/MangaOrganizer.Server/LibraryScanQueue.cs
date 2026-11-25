using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed class LibraryScanQueue(LibraryStore store, ILogger<LibraryScanQueue> logger) : BackgroundService
{
    private readonly Channel<LibraryRoot> requests = Channel.CreateUnbounded<LibraryRoot>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    public bool TryQueue(LibraryRoot root) => store.TryQueue(root.Id) && requests.Writer.TryWrite(root);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var root in requests.Reader.ReadAllAsync(stoppingToken))
        {
            stoppingToken.ThrowIfCancellationRequested();
            store.SetState(root.Id, "scanning");
            try
            {
                LibraryScanner.RequireRoot(root.Path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                RecordFailure(root.Id, "unavailable", error);
                continue;
            }

            try
            {
                store.Refresh(root, stoppingToken);
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
        }
    }

    private void RecordFailure(Guid rootId, string state, Exception error)
    {
        logger.LogWarning("Scan for library root {RootId} ended in state {State} ({ErrorType}).",
            rootId, state, error.GetType().Name);
        store.SetState(rootId, state);
    }
}
