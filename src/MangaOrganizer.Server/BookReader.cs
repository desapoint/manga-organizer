using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace MangaOrganizer.Server;

internal sealed record ArchivePage(string Name, int Width, int Height, byte[] Hash);
internal sealed record ReaderBook(string Title, IReadOnlyList<ArchivePage> Pages);

internal sealed class BookReader(LibraryStore store) : IDisposable
{
    private const int MaxPageBytes = 16 * 1024 * 1024;
    private const long MaxDecodedArchiveBytes = 768L * 1024 * 1024;
    private readonly MemoryCache manifests = new(new MemoryCacheOptions { SizeLimit = 24_000 });

    public ReaderBook? Open(string contentId, CancellationToken cancellationToken)
    {
        foreach (var source in store.ReadSources(contentId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = FindCurrentFile(source);
            if (file is null) continue;

            if (!manifests.TryGetValue<IReadOnlyList<ArchivePage>>(contentId, out var pages) || pages is null)
            {
                pages = ReadArchive(file, source, stream => BuildManifest(stream, source.Hash, cancellationToken));
                if (pages is null) continue;
                manifests.Set(contentId, pages, new MemoryCacheEntryOptions { Size = pages.Count, SlidingExpiration = TimeSpan.FromMinutes(12) });
            }

            return new(file.Name, pages);
        }
        return null;
    }

    public RasterImage? Page(string contentId, ArchivePage expected, CancellationToken cancellationToken)
    {
        foreach (var source in store.ReadSources(contentId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = FindCurrentFile(source);
            if (file is null) continue;

            var page = ReadArchive(file, source, stream =>
            {
                using var archive = ArchivePages.Open(stream);
                if (archive is null) return null;
                var entry = ArchivePages.Entries(archive).FirstOrDefault(entry => entry.FullName == expected.Name);
                return entry is null ? null : ArchivePages.Read(entry, MaxPageBytes);
            });
            if (page is not null && SHA256.HashData(page.Bytes).AsSpan().SequenceEqual(expected.Hash))
            {
                return page;
            }
        }
        return null;
    }

    public void Dispose() => manifests.Dispose();

    private static FileInfo? FindCurrentFile(ReaderSource source)
    {
        try
        {
            var file = MediaFiles.Resolve(source.RootPath, source.RelativePath);
            return file is { Exists: true } && file.Length == source.Size && file.LastWriteTimeUtc == source.ModifiedAt.UtcDateTime
                ? file : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static T? ReadArchive<T>(FileInfo file, ReaderSource source, Func<FileStream, T?> read) where T : class
    {
        try
        {
            return MediaFiles.Read(file, source.Size, source.ModifiedAt, read);
        }
        catch (Exception error) when (ArchivePages.IsUnreadable(error))
        {
            return null;
        }
    }

    private static IReadOnlyList<ArchivePage>? BuildManifest(Stream stream, byte[] expectedHash, CancellationToken cancellationToken)
    {
        if (!MediaFiles.Hash(stream, cancellationToken).AsSpan().SequenceEqual(expectedHash)) return null;
        stream.Position = 0;
        using var archive = ArchivePages.Open(stream);
        if (archive is null) return null;

        var pages = new List<ArchivePage>();
        long readBytes = 0;
        foreach (var entry in ArchivePages.Entries(archive))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Length > MaxPageBytes) continue;
            readBytes += entry.Length;
            if (readBytes > MaxDecodedArchiveBytes) return null;
            if (ArchivePages.Read(entry, MaxPageBytes) is { } page)
            {
                pages.Add(new(entry.FullName, page.Width, page.Height, SHA256.HashData(page.Bytes)));
            }
        }

        return pages.Count > 0 ? pages : null;
    }
}
