using System.Buffers.Binary;

namespace MangaOrganizer.Server;

internal sealed record RasterImage(byte[] Bytes, string MediaType, int Width, int Height)
{
    public const int MaxCoverBytes = 8 * 1024 * 1024;
    private const int MaxDimension = 16000;
    private const int MaxPixels = 24_000_000;

    public static bool IsPageName(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp";

    public static RasterImage? Read(Stream stream, long length, int maxBytes)
    {
        if (length < 24 || length > maxBytes)
        {
            return null;
        }

        var bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            return null;
        }

        ReadOnlySpan<byte> image = bytes;
        var dimensions = Png(image) ?? Jpeg(image) ?? WebP(image);
        if (dimensions is not { } result || result.Width is < 1 or > MaxDimension || result.Height is < 1 or > MaxDimension ||
            (long)result.Width * result.Height > MaxPixels)
        {
            return null;
        }

        return new(bytes, result.MediaType, result.Width, result.Height);
    }

    private static (string MediaType, int Width, int Height)? Png(ReadOnlySpan<byte> image)
    {
        if (image.Length < 45 || !image[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !image.Slice(12, 4).SequenceEqual("IHDR"u8) || !image.Slice(image.Length - 8, 4).SequenceEqual("IEND"u8))
        {
            return null;
        }

        return ("image/png", BinaryPrimitives.ReadInt32BigEndian(image[16..]), BinaryPrimitives.ReadInt32BigEndian(image[20..]));
    }

    private static (string MediaType, int Width, int Height)? Jpeg(ReadOnlySpan<byte> image)
    {
        if (image[0] != 0xFF || image[1] != 0xD8 || image[^2] != 0xFF || image[^1] != 0xD9)
        {
            return null;
        }

        var offset = 2;
        while (offset + 3 < image.Length && image[offset++] == 0xFF)
        {
            if (image[offset] == 0xFF)
            {
                continue;
            }
            var marker = image[offset++];
            var length = BinaryPrimitives.ReadUInt16BigEndian(image[offset..]);
            if (length < 2 || length > image.Length - offset || marker is 0xDA or 0xD9)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC) && length >= 8)
            {
                return ("image/jpeg", BinaryPrimitives.ReadUInt16BigEndian(image[(offset + 5)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(image[(offset + 3)..]));
            }

            offset += length;
        }

        return null;
    }

    private static (string MediaType, int Width, int Height)? WebP(ReadOnlySpan<byte> image)
    {
        if (image.Length < 30 || !image[..4].SequenceEqual("RIFF"u8) || !image.Slice(8, 4).SequenceEqual("WEBP"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(image[4..]) != image.Length - 8)
        {
            return null;
        }

        var firstChunk = image.Slice(12, 4);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(image[16..]);
        if (length > image.Length - 20)
        {
            return null;
        }

        if (firstChunk.SequenceEqual("VP8X"u8) && length >= 10)
        {
            return ("image/webp", 1 + ReadUInt24(image[24..]), 1 + ReadUInt24(image[27..]));
        }
        if (firstChunk.SequenceEqual("VP8L"u8) && length >= 5 && image[20] == 0x2F)
        {
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(image[21..]);
            return ("image/webp", 1 + (int)(packed & 0x3FFF), 1 + (int)((packed >> 14) & 0x3FFF));
        }
        if (firstChunk.SequenceEqual("VP8 "u8) && length >= 10 && image[23] == 0x9D && image[24] == 0x01 && image[25] == 0x2A)
        {
            return ("image/webp", BinaryPrimitives.ReadUInt16LittleEndian(image[26..]) & 0x3FFF,
                BinaryPrimitives.ReadUInt16LittleEndian(image[28..]) & 0x3FFF);
        }

        return null;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data) => data[0] | (data[1] << 8) | (data[2] << 16);
}
