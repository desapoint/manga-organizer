using System.Buffers.Binary;

namespace MangaOrganizer.Server;

// Check archive sizes before the decoder allocates a compression dictionary or loads service blocks.
internal static class RarBounds
{
    private const int MaxHeader = 256 * 1024;
    private const int MaxServiceBytes = 512 * 1024;
    private const int MaxDictionaryShift = 10; // RAR5's 128 KiB base multiplied by 2^10.

    public static bool CanOpen(Stream stream, bool rar5)
    {
        stream.Position = rar5 ? 8 : 7;
        return rar5 ? CheckRar5(stream) : CheckRar4(stream);
    }

    private static bool CheckRar4(Stream stream)
    {
        const int basicHeader = 7;
        var buffer = new byte[ushort.MaxValue];
        var headerBytes = 0;
        for (var count = 0; count < ArchivePages.MaxEntryCount + 16 && stream.Position < stream.Length; count++)
        {
            stream.ReadExactly(buffer.AsSpan(0, basicHeader));
            var type = buffer[2];
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(3));
            var size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(5));
            headerBytes += size;
            if (size < basicHeader || headerBytes > ArchivePages.MaxCentralDirectory || (count == 0 && type != 0x73)) return false;
            stream.ReadExactly(buffer.AsSpan(basicHeader, size - basicHeader));
            if (type == 0x73 && (flags & 0x0089) != 0) return false; // Volume, solid, or encrypted headers.
            if (type == 0x7b) return true;

            ulong data = 0;
            if ((flags & 0x8000) != 0)
            {
                if (size < 11) return false;
                data = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(7));
                if (type is 0x74 or 0x7a && (flags & 0x0100) != 0)
                {
                    if (size < 40) return false;
                    data |= (ulong)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(32)) << 32;
                }
            }
            if (type == 0x7a && data > MaxServiceBytes || !Skip(stream, data)) return false;
        }
        return stream.Position == stream.Length && headerBytes >= basicHeader;
    }

    private static bool CheckRar5(Stream stream)
    {
        var buffer = new byte[MaxHeader];
        Span<byte> encoded = stackalloc byte[10];
        var headerBytes = 0;
        for (var count = 0; count < ArchivePages.MaxEntryCount + 16 && stream.Position < stream.Length; count++)
        {
            if (!Skip(stream, 4)) return false; // Header CRC field.
            var end = 0;
            while (end < encoded.Length)
            {
                var next = stream.ReadByte();
                if (next < 0) return false;
                encoded[end++] = (byte)next;
                if (next < 128) break;
            }
            ReadOnlySpan<byte> length = encoded[..end];
            if (!ReadNumber(ref length, out var size) || size is 0 or > MaxHeader) return false;
            headerBytes += (int)size;
            if (headerBytes > ArchivePages.MaxCentralDirectory) return false;
            stream.ReadExactly(buffer.AsSpan(0, (int)size));
            ReadOnlySpan<byte> header = buffer.AsSpan(0, (int)size);
            if (!ReadNumber(ref header, out var type) || !ReadNumber(ref header, out var flags) ||
                (flags & 1) != 0 && !ReadNumber(ref header, out _) || count == 0 && type != 1)
            {
                return false;
            }
            ulong data = 0;
            if ((flags & 2) != 0 && !ReadNumber(ref header, out data)) return false;
            if (type == 5) return true;
            if (type == 4 || type == 1 && (!ReadNumber(ref header, out var archiveFlags) || (archiveFlags & 5) != 0)) return false;
            if (type is 2 or 3 && !CheckFile5(header)) return false;
            if (type == 3 && data > MaxServiceBytes || !Skip(stream, data)) return false;
        }
        return false;
    }

    private static bool CheckFile5(ReadOnlySpan<byte> header)
    {
        if (!ReadNumber(ref header, out var flags) || !ReadNumber(ref header, out _) || !ReadNumber(ref header, out _)) return false;
        var optionalBytes = ((flags & 2) == 0 ? 0 : 4) + ((flags & 4) == 0 ? 0 : 4);
        if (header.Length < optionalBytes) return false;
        header = header[optionalBytes..];
        return ReadNumber(ref header, out var compression) &&
            (compression & 0x40) == 0 && ((compression >> 10) & 15) <= MaxDictionaryShift;
    }

    private static bool ReadNumber(ref ReadOnlySpan<byte> bytes, out ulong value)
    {
        value = 0;
        for (var index = 0; index < Math.Min(bytes.Length, 10); index++)
        {
            var next = bytes[index];
            if (index == 9 && next > 1) return false;
            value |= (ulong)(next & 127) << (7 * index);
            if (next >= 128) continue;
            bytes = bytes[(index + 1)..];
            return true;
        }
        return false;
    }

    private static bool Skip(Stream stream, ulong count)
    {
        if (count > (ulong)(stream.Length - stream.Position)) return false;
        stream.Position += (long)count;
        return true;
    }
}
