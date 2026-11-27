using System.Globalization;
using System.Security.Cryptography;

namespace MangaOrganizer.Server;

internal sealed record CoverSource(string RelativePath, long Size, DateTimeOffset ModifiedAt, string? ArchiveEntry, byte[] Sha256, string? Stamp = null);

internal sealed record ArchiveCoverInspection(CoverSource? Cover);

internal static class CoverArt
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp"];

    public static IReadOnlyList<FileInfo> Sidecars(DirectoryInfo directory)
    {
        try
        {
            if (!directory.Exists) return [];
            LibraryScanner.RequireRoot(directory.FullName);
            return directory.EnumerateFiles("cover*", new EnumerationOptions
                {
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MatchCasing = MatchCasing.CaseInsensitive,
                })
                .Select(file => (File: file, Number: SidecarNumber(file.Name)))
                .Where(item => item.Number.HasValue)
                .OrderBy(item => item.Number)
                .ThenBy(item => Array.IndexOf(Extensions, item.File.Extension.ToLowerInvariant()))
                .ThenBy(item => item.File.Name, StringComparer.Ordinal)
                .Select(item => item.File).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static int? SidecarNumber(string name)
    {
        if (!RasterImage.IsPageName(name)) return null;
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.Equals("cover", StringComparison.OrdinalIgnoreCase)) return 0;
        return stem.StartsWith("cover-", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(stem.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
    }

    public static IEnumerable<CoverSource> FindSidecars(string root, string directory, CoverCache cache,
        Func<string, string, byte[]?>? cachedSidecar = null)
    {
        foreach (var file in Sidecars(new DirectoryInfo(Path.Combine(root, directory))))
        {
            CoverSource? cover = null;
            try
            {
                var size = file.Length;
                DateTimeOffset modified = file.LastWriteTimeUtc;
                var path = Path.GetRelativePath(root, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
                cover = RegularFile.Read(file, size, modified.UtcDateTime, handle =>
                {
                    var stamp = cachedSidecar is null ? null : RegularFile.ScanStamp(handle);
                    if (stamp is not null && cachedSidecar?.Invoke(path, stamp) is { } saved)
                        return new CoverSource(path, size, modified, null, saved, stamp);

                    using var stream = new FileStream(handle, FileAccess.Read);
                    return stream.Length == size && RasterImage.Read(stream, size, RasterImage.MaxCoverBytes) is { } image
                        && cache.Store(image) is { } digest ? new CoverSource(path, size, modified, null, digest, stamp) : null;
                });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A cover can disappear while a library is being scanned.
            }
            if (cover is not null) yield return cover;
        }
    }

    public static ArchiveCoverInspection? InspectArchiveCover(CoverCache cache, string root, string relativePath, long size,
        DateTimeOffset modifiedAt, string? stamp, CancellationToken cancellationToken)
    {
        var file = MediaFiles.Resolve(root, relativePath);
        if (file is null) return null;

        try
        {
            return RegularFile.Read(file, size, modifiedAt.UtcDateTime, handle =>
            {
                if (stamp is not null && RegularFile.ScanStamp(handle) != stamp) return null;
                using var stream = new FileStream(handle, FileAccess.Read);
                if (stream.Length != size) return null;
                using var archive = ArchivePages.Open(stream);
                if (archive is null) return null;

                var pages = ArchivePages.Entries(archive).ToArray();
                if (pages.Length == 0) return new ArchiveCoverInspection(null);

                var designated = ComicInfoCover(archive);
                var candidates = new List<ComicEntry>();
                if (designated is >= 0 && designated < pages.Length) candidates.Add(pages[designated.Value]);
                candidates.AddRange(pages.Where(IsNamedCover));
                candidates.AddRange(pages.Take(24));
                foreach (var entry in candidates.Distinct())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ArchivePages.Read(entry, RasterImage.MaxCoverBytes) is { } image && cache.Store(image) is { } digest)
                        return new ArchiveCoverInspection(new(relativePath, size, modifiedAt, entry.FullName, digest));
                }

                return null;
            });
        }
        catch (Exception error) when (ArchivePages.IsUnreadable(error))
        {
            return null;
        }
    }

    internal static bool IsNamedCover(ComicEntry entry) =>
        Path.GetFileNameWithoutExtension(entry.Name).ToLowerInvariant() is "cover" or "front-cover" or "front_cover" or "front cover" or "frontcover";

    private static int? ComicInfoCover(ComicArchive archive)
    {
        var info = ComicInfoXml.Read(archive);
        if (info is null) return null;
        var ns = info.Name.Namespace;
        var cover = info.Element(ns + "Pages")?.Elements(ns + "Page").FirstOrDefault(page =>
            string.Equals((string?)page.Attribute("Type"), "FrontCover", StringComparison.OrdinalIgnoreCase));
        return int.TryParse((string?)cover?.Attribute("Image"), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index : null;
    }

    public static RasterImage? Read(string root, CoverSource source)
    {
        var file = MediaFiles.Resolve(root, source.RelativePath);
        if (file is null) return null;

        var image = source.ArchiveEntry is null ? ReadSidecar(file, source.Size, source.ModifiedAt) :
            WithArchive(file, source.Size, source.ModifiedAt, stream =>
        {
            using var archive = ArchivePages.Open(stream);
            var entry = archive is null ? null : ArchivePages.Entries(archive).FirstOrDefault(entry => entry.FullName == source.ArchiveEntry);
            return entry is null ? null : ArchivePages.Read(entry, RasterImage.MaxCoverBytes);
        });
        return image is not null && SHA256.HashData(image.Bytes).AsSpan().SequenceEqual(source.Sha256) ? image : null;
    }

    private static RasterImage? ReadSidecar(FileInfo file, long size, DateTimeOffset modifiedAt) =>
        MediaFiles.Read(file, size, modifiedAt, stream => RasterImage.Read(stream, size, RasterImage.MaxCoverBytes));

    private static T? WithArchive<T>(FileInfo file, long size, DateTimeOffset modifiedAt, Func<FileStream, T?> read) where T : class
    {
        try
        {
            return MediaFiles.Read(file, size, modifiedAt, read);
        }
        catch (Exception error) when (ArchivePages.IsUnreadable(error))
        {
            return null;
        }
    }
}
