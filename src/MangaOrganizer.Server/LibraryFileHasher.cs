using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace MangaOrganizer.Server;

internal sealed record IndexedDigest(byte[] Digest, string? Stamp, string? Chapters, string? ChapterSource, bool Reused = false);

internal static class LibraryFileHasher
{
    public static byte[]? Read(FileInfo file, long size, DateTime modifiedAt, byte[] buffer, CancellationToken cancellationToken,
        Action<int>? onBytesRead = null) =>
        RegularFile.Read(file, size, modifiedAt, handle => ReadDigest(handle, size, buffer, cancellationToken, onBytesRead));

    public static IndexedDigest? ReadForIndex(FileInfo file, long size, DateTime modifiedAt, byte[] buffer,
        CancellationToken cancellationToken, Action<int> onBytesRead, string? volumePath, IndexedDigest? cached) =>
        RegularFile.Read(file, size, modifiedAt, handle =>
        {
            var stamp = RegularFile.ScanStamp(handle);
            if (stamp is not null && cached?.Stamp == stamp) return cached with { Reused = true };

            var digest = ReadDigest(handle, size, buffer, cancellationToken, onBytesRead);
            if (digest is null) return null;
            DetectedVolumeChapters? coverage = null;
            if (volumePath is not null)
            {
                try
                {
                    using var stream = new FileStream(handle, FileAccess.Read);
                    coverage = VolumeChapterDetection.Read(stream, volumePath, cancellationToken);
                }
                catch (Exception error) when (ArchivePages.IsUnreadable(error))
                {
                    // The digest remains valid when chapter detection cannot open the archive.
                }
            }
            return new IndexedDigest(digest, stamp, coverage is null ? null : JsonSerializer.Serialize(coverage.Chapters), coverage?.Source);
        });

    private static byte[]? ReadDigest(SafeFileHandle handle, long size, byte[] buffer, CancellationToken cancellationToken, Action<int>? onBytesRead)
    {
        // Use offset-based reads so the handle remains open for validation after hashing.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long position = 0;
        while (position < size)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, size - position)), position);
            if (read == 0)
            {
                return null;
            }

            hash.AppendData(buffer.AsSpan(0, read));
            onBytesRead?.Invoke(read);
            position += read;
        }

        return RandomAccess.GetLength(handle) == size ? hash.GetHashAndReset() : null;
    }
}
