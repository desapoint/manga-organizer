using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace MangaOrganizer.Server;

internal static class LibraryFileHasher
{
    public static byte[]? Read(FileInfo file, long size, DateTime modifiedAt, byte[] buffer, CancellationToken cancellationToken) =>
        RegularFile.Read(file, size, modifiedAt, handle => ReadDigest(handle, size, buffer, cancellationToken));

    private static byte[]? ReadDigest(SafeFileHandle handle, long size, byte[] buffer, CancellationToken cancellationToken)
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
            position += read;
        }

        return RandomAccess.GetLength(handle) == size ? hash.GetHashAndReset() : null;
    }
}
