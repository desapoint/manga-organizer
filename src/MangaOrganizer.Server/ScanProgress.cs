namespace MangaOrganizer.Server;

internal sealed record ScanSnapshot(string Phase, long DiscoveredFiles, long ProcessedFiles, long VerifiedFiles,
    long TotalBytes, long BytesRead, string? CurrentPath, long SeriesTotal, long SeriesChecked, long CoversFound);

internal sealed class ScanProgress
{
    private readonly object gate = new();
    private string phase = "queued";
    private long discoveredFiles;
    private long processedFiles;
    private long verifiedFiles;
    private long totalBytes;
    private long bytesRead;
    private string? currentPath;
    private long seriesTotal;
    private long seriesChecked;
    private long coversFound;

    public ScanSnapshot Snapshot()
    {
        lock (gate)
        {
            return new(phase, discoveredFiles, processedFiles, verifiedFiles, totalBytes, bytesRead, currentPath,
                seriesTotal, seriesChecked, coversFound);
        }
    }

    public void StartPhase(string next)
    {
        lock (gate)
        {
            phase = next;
            currentPath = null;
        }
    }

    public void Discovered(long size)
    {
        lock (gate)
        {
            discoveredFiles++;
            totalBytes += size;
        }
    }

    public void BeginFile(string relativePath)
    {
        lock (gate) currentPath = relativePath;
    }

    public void ReadBytes(int count)
    {
        lock (gate) bytesRead += count;
    }

    public void FinishedFile(bool verified)
    {
        lock (gate)
        {
            processedFiles++;
            if (verified) verifiedFiles++;
        }
    }

    public void BeginCovers(long groups)
    {
        lock (gate)
        {
            phase = "covers";
            currentPath = null;
            seriesTotal = groups;
        }
    }

    public void CheckedCover(bool found)
    {
        lock (gate)
        {
            seriesChecked++;
            if (found) coversFound++;
        }
    }
}
