using Microsoft.Data.Sqlite;

namespace MangaOrganizer.Server;

internal sealed class MetadataSuggestionWorker(LibraryStore store, MetadataAgent agent, ILogger<MetadataSuggestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (store.NextMetadataSuggestion() is { } work)
                    await LookUp(work, stoppingToken);
            }
            catch (SqliteException error)
            {
                logger.LogWarning("Could not update the AniList suggestion queue (SQLite error {ErrorCode}).", error.SqliteErrorCode);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task LookUp(MetadataWork work, CancellationToken stoppingToken)
    {
        try
        {
            var title = SeriesSuggestions.Key("", work.Title);
            var matches = await agent.Search(work.Title, stoppingToken);
            var ranked = matches.DistinctBy(match => match.ProviderId).Select(match => new MetadataProposal(match,
                match.LookupTitles.FirstOrDefault(candidate => SeriesSuggestions.Key("", candidate) == title)))
                .OrderByDescending(candidate => candidate.MatchingTitle is not null).ToArray();
            store.FinishMetadataSuggestion(work, ranked, null);
        }
        catch (MetadataLookupException error)
        {
            store.FinishMetadataSuggestion(work, null, error.Message, error.RetryAt);
        }
    }
}
