using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace MangaOrganizer.Server;

internal sealed record CoverSource(string RelativePath, long Size, DateTimeOffset ModifiedAt, string? ArchiveEntry, byte[] Sha256);

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

    public static IEnumerable<CoverSource> FindSidecars(string root, string directory, CoverCache cache)
    {
        foreach (var file in Sidecars(new DirectoryInfo(Path.Combine(root, directory))))
        {
            CoverSource? cover = null;
            try
            {
                var size = file.Length;
                DateTimeOffset modified = file.LastWriteTimeUtc;
                if (ReadSidecar(file, size, modified) is { } image && cache.Store(image) is { } digest)
                {
                    var path = Path.GetRelativePath(root, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
                    cover = new(path, size, modified, null, digest);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A cover can disappear while a library is being scanned.
            }
            if (cover is not null) yield return cover;
        }
    }

    public static CoverSource? FindArchiveCover(CoverCache cache, string root, string relativePath, long size, DateTimeOffset modifiedAt,
        CancellationToken cancellationToken)
    {
        var file = MediaFiles.Resolve(root, relativePath);
        if (file is null) return null;

        return WithArchive(file, size, modifiedAt, stream =>
        {
            using var archive = ArchivePages.Open(stream);
            if (archive is null) return null;

            var pages = ArchivePages.Entries(archive).ToArray();
            var designated = ComicInfoCover(archive);
            var candidates = new List<ZipArchiveEntry>();
            if (designated is >= 0 && designated < pages.Length) candidates.Add(pages[designated.Value]);
            candidates.AddRange(pages.Where(IsNamedCover));
            candidates.AddRange(pages.Take(24));
            foreach (var entry in candidates.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ArchivePages.Read(entry, RasterImage.MaxCoverBytes) is { } image && cache.Store(image) is { } digest)
                {
                    return new CoverSource(relativePath, size, modifiedAt, entry.FullName, digest);
                }
            }

            return null;
        });
    }

    private static bool IsNamedCover(ZipArchiveEntry entry) =>
        Path.GetFileNameWithoutExtension(entry.Name).ToLowerInvariant() is "cover" or "front-cover" or "front_cover" or "front cover" or "frontcover";

    private static int? ComicInfoCover(ZipArchive archive)
    {
        const int maxMetadataBytes = 256 * 1024;
        var metadata = archive.Entries.Where(entry => entry.Name.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName.Contains('/')).FirstOrDefault();
        if (metadata is null || metadata.Length is <= 0 or > maxMetadataBytes) return null;

        try
        {
            using var stream = metadata.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = maxMetadataBytes,
            });
            var info = XDocument.Load(reader).Root;
            if (info?.Name.LocalName != "ComicInfo") return null;
            var ns = info.Name.Namespace;
            var cover = info.Element(ns + "Pages")?.Elements(ns + "Page").FirstOrDefault(page =>
                string.Equals((string?)page.Attribute("Type"), "FrontCover", StringComparison.OrdinalIgnoreCase));
            return int.TryParse((string?)cover?.Attribute("Image"), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                ? index : null;
        }
        catch (Exception error) when (error is XmlException or IOException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
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
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }
}
