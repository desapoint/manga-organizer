using System.Diagnostics;

namespace MangaOrganizer.Server;

internal sealed record ScanSnapshot(string Phase, long DiscoveredFiles, long ProcessedFiles, long VerifiedFiles,
    long TotalBytes, long BytesRead, string? CurrentPath, long SeriesTotal, long SeriesChecked, long CoversFound,
    long HashedFiles, long ReusedFiles, long ElapsedMilliseconds, bool Full);

internal sealed class ScanProgress(bool full)
{
    public bool Full { get; } = full;
    private long startedAt;
    private readonly object gate = new();
    private string phase = "queued";
    private long discoveredFiles;
    private long processedFiles;
    private long verifiedFiles;
    private long totalBytes;
    private long bytesRead;
    private long reusedFiles;
    private string? currentPath;
    private long seriesTotal;
    private long seriesChecked;
    private long coversFound;

    public ScanSnapshot Snapshot()
    {
        lock (gate)
        {
            return new(phase, discoveredFiles, processedFiles, verifiedFiles, totalBytes, bytesRead, currentPath,
                seriesTotal, seriesChecked, coversFound, verifiedFiles - reusedFiles, reusedFiles,
                startedAt == 0 ? 0 : (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, Full);
        }
    }

    public void StartPhase(string next)
    {
        lock (gate)
        {
            if (startedAt == 0) startedAt = Stopwatch.GetTimestamp();
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

    public void FinishedFile(bool verified, bool reused)
    {
        lock (gate)
        {
            processedFiles++;
            if (verified) verifiedFiles++;
            if (reused) reusedFiles++;
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
