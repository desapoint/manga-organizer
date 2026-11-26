namespace MangaOrganizer.Server;

internal static class MediaFiles
{
    public static FileInfo? Resolve(string root, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath)) return null;

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        return LibraryScanner.Contains(root, fullPath) ? new FileInfo(fullPath) : null;
    }

    public static T? Read<T>(FileInfo file, long size, DateTimeOffset modifiedAt, Func<FileStream, T?> read) where T : class =>
        RegularFile.Read(file, size, modifiedAt.UtcDateTime, handle =>
        {
            using var stream = new FileStream(handle, FileAccess.Read);
            return stream.Length == size ? read(stream) : null;
        });
}
