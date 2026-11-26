using System.Security.Cryptography;

namespace MangaOrganizer.Server;

internal sealed record CoverSource(string RelativePath, long Size, DateTimeOffset ModifiedAt, string? ArchiveEntry, byte[] Sha256);

internal static class CoverArt
{
    public static CoverSource? FindSidecar(string root, string directory)
    {
        var relativePath = directory.Length == 0 ? "cover.png" : directory + "/cover.png";
        var file = MediaFiles.Resolve(root, relativePath);
        if (file is not { Exists: true }) return null;

        try
        {
            var size = file.Length;
            DateTimeOffset modifiedAt = file.LastWriteTimeUtc;
            var image = ReadSidecar(file, size, modifiedAt);
            return image is null ? null : new CoverSource(relativePath, size, modifiedAt, null, SHA256.HashData(image.Bytes));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static CoverSource? FindFirstPage(string root, string relativePath, long size, DateTimeOffset modifiedAt,
        CancellationToken cancellationToken)
    {
        var file = MediaFiles.Resolve(root, relativePath);
        if (file is null) return null;

        return WithArchive(file, size, modifiedAt, stream =>
        {
            using var archive = ArchivePages.Open(stream);
            if (archive is null) return null;

            foreach (var entry in ArchivePages.Entries(archive).Take(24))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ArchivePages.Read(entry, RasterImage.MaxCoverBytes) is { } image)
                {
                    return new CoverSource(relativePath, size, modifiedAt, entry.FullName, SHA256.HashData(image.Bytes));
                }
            }

            return null;
        });
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
