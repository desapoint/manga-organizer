using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace MangaOrganizer.Server;

internal sealed record IndexedDigest(byte[] Digest, DetectedVolumeChapters? Coverage);

internal static class LibraryFileHasher
{
    public static byte[]? Read(FileInfo file, long size, DateTime modifiedAt, byte[] buffer, CancellationToken cancellationToken,
        Action<int>? onBytesRead = null) =>
        RegularFile.Read(file, size, modifiedAt, handle => ReadDigest(handle, size, buffer, cancellationToken, onBytesRead));

    public static IndexedDigest? ReadForIndex(FileInfo file, long size, DateTime modifiedAt, byte[] buffer,
        CancellationToken cancellationToken, Action<int> onBytesRead, string? volumePath) =>
        RegularFile.Read(file, size, modifiedAt, handle =>
        {
            var digest = ReadDigest(handle, size, buffer, cancellationToken, onBytesRead);
            if (digest is null) return null;
            if (volumePath is null) return new IndexedDigest(digest, null);
            try
            {
                using var stream = new FileStream(handle, FileAccess.Read);
                return new IndexedDigest(digest, VolumeChapterDetection.Read(stream, volumePath, cancellationToken));
            }
            catch (Exception error) when (ArchivePages.IsUnreadable(error))
            {
                return new IndexedDigest(digest, null);
            }
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
