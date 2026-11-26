using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace MangaOrganizer.Server;

internal static class ArchivePages
{
    private const int MaxEntryCount = 4096;
    private const uint MaxCentralDirectory = 4 * 1024 * 1024;
    private static readonly StringComparer PageOrder = CultureInfo.InvariantCulture.CompareInfo.GetStringComparer(
        CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    public static bool Supports(string extension) => extension is "cbz" or "zip";

    public static ZipArchive? Open(Stream stream)
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

    public static IEnumerable<ZipArchiveEntry> Entries(ZipArchive archive) => archive.Entries
        .Where(IsPage)
        .GroupBy(entry => entry.FullName, StringComparer.Ordinal)
        .Where(group => group.Count() == 1)
        .Select(group => group.Single())
        .OrderBy(entry => entry.FullName, PageOrder)
        .ThenBy(entry => entry.FullName, StringComparer.Ordinal);

    public static RasterImage? Read(ZipArchiveEntry entry, int maxBytes)
    {
        if (entry.Length > maxBytes) return null;

        try
        {
            using var stream = entry.Open();
            return RasterImage.Read(stream, entry.Length, maxBytes);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsPage(ZipArchiveEntry entry)
    {
        var name = entry.FullName;
        return RasterImage.IsPageName(name) && !name.Contains('\\') && !name.Contains(':') && !name.Contains('\0') &&
            ((entry.ExternalAttributes >> 16) & 0xF000) is 0 or 0x8000 &&
            name.Split('/').All(part => part.Length > 0 && !part.StartsWith('.') && part != "__MACOSX");
    }
}
