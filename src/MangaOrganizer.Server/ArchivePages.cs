using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MangaOrganizer.Server;

internal sealed record ComicEntry(string FullName, long Length, Func<Stream> Open)
{
    public string Name => Path.GetFileName(FullName);
}

internal sealed class ComicArchive(IDisposable backend, IReadOnlyList<ComicEntry> entries) : IDisposable
{
    public IReadOnlyList<ComicEntry> Entries { get; } = entries;
    public void Dispose() => backend.Dispose();
}

internal static class ArchivePages
{
    internal const int MaxEntryCount = 4096;
    internal const uint MaxCentralDirectory = 4 * 1024 * 1024;
    private static readonly StringComparer PageOrder = CultureInfo.InvariantCulture.CompareInfo.GetStringComparer(
        CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    public static bool Supports(string extension) => extension is "cbz" or "zip" or "cbr" or "rar";

    public static ComicArchive? Open(Stream stream)
    {
        IDisposable? backend = null;
        try
        {
            Span<byte> signature = stackalloc byte[8];
            stream.Position = 0;
            var count = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
            var isRar4 = signature[..count].StartsWith("Rar!\u001a\u0007\0"u8);
            var isRar5 = signature[..count].StartsWith("Rar!\u001a\u0007\u0001\0"u8);
            IEnumerable<ComicEntry> entries;
            if (isRar4 || isRar5)
            {
                if (!RarBounds.CanOpen(stream, isRar5)) return null;
                stream.Position = 0;
                var rar = RarArchive.Open(stream, new ReaderOptions { LeaveStreamOpen = true });
                backend = rar;
                entries = rar.Entries.Where(entry => !entry.IsDirectory && entry.IsComplete && !entry.IsEncrypted && entry.Key is not null)
                    .Select(entry => new ComicEntry(entry.Key!.Replace('\\', '/'), entry.Size, entry.OpenEntryStream));
            }
            else
            {
                var zip = OpenZip(stream);
                if (zip is null) return null;
                backend = zip;
                entries = zip.Entries.Where(entry => ((entry.ExternalAttributes >> 16) & 0xF000) is 0 or 0x8000)
                    .Select(entry => new ComicEntry(entry.FullName, entry.Length, entry.Open));
            }
            var result = new ComicArchive(backend, entries.Where(entry => IsSafeName(entry.FullName)).ToArray());
            backend = null;
            return result;
        }
        catch (Exception error) when (IsUnreadable(error))
        {
            return null;
        }
        finally
        {
            backend?.Dispose();
        }
    }

    private static ZipArchive? OpenZip(Stream stream)
    {
        // Read the bounded ZIP footer before ZipArchive loads the central directory.
        const int footerSize = 22;
        if (stream.Length < footerSize) return null;

        var buffer = new byte[(int)Math.Min(stream.Length, ushort.MaxValue + footerSize)];
        var tailOffset = stream.Length - buffer.Length;
        stream.Position = tailOffset;
        stream.ReadExactly(buffer);
        for (var offset = buffer.Length - footerSize; offset >= 0; offset--)
        {
            ReadOnlySpan<byte> footer = buffer.AsSpan(offset);
            if (BinaryPrimitives.ReadUInt32LittleEndian(footer) != 0x06054B50 ||
                BinaryPrimitives.ReadUInt16LittleEndian(footer[20..]) != footer.Length - footerSize)
            {
                continue;
            }

            var entries = BinaryPrimitives.ReadUInt16LittleEndian(footer[10..]);
            var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(footer[12..]);
            var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(footer[16..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(footer[4..]) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(footer[8..]) != entries || entries > MaxEntryCount ||
                directorySize > MaxCentralDirectory || (long)directoryOffset + directorySize != tailOffset + offset)
            {
                return null;
            }

            stream.Position = 0;
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }

        return null;
    }

    public static IEnumerable<ComicEntry> Entries(ComicArchive archive) => archive.Entries
        .Where(entry => RasterImage.IsPageName(entry.FullName))
        .GroupBy(entry => entry.FullName, StringComparer.Ordinal)
        .Where(group => group.Count() == 1)
        .Select(group => group.Single())
        .OrderBy(entry => entry.FullName, PageOrder)
        .ThenBy(entry => entry.FullName, StringComparer.Ordinal);

    public static RasterImage? Read(ComicEntry entry, int maxBytes)
    {
        if (entry.Length > maxBytes) return null;

        try
        {
            using var stream = entry.Open();
            return RasterImage.Read(stream, entry.Length, maxBytes);
        }
        catch (Exception error) when (IsUnreadable(error))
        {
            return null;
        }
    }

    public static bool IsUnreadable(Exception error) => error is IOException or InvalidDataException or NotSupportedException or
        InvalidFormatException or IncompleteArchiveException or MultipartStreamRequiredException or System.Security.Cryptography.CryptographicException;

    private static bool IsSafeName(string name) => !name.Contains('\\') && !name.Contains(':') && !name.Contains('\0') &&
        name.Split('/').All(part => part.Length > 0 && !part.StartsWith('.') && part != "__MACOSX");
}
