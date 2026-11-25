namespace MangaOrganizer.Server;

internal sealed record LibraryFileFact(string RelativePath, string Extension, long Size, DateTimeOffset ModifiedAt);

internal static class LibraryScanner
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cbz", ".cbr", ".cb7", ".zip", ".rar", ".7z", ".pdf", ".epub",
    };

    public static IReadOnlyList<string> ReadRoots(IConfiguration configuration, string dataDirectory)
    {
        var paths = new List<string>();
        foreach (var setting in configuration.GetSection("MANGA_ORGANIZER_LIBRARY_ROOTS").GetChildren())
        {
            if (setting.Value is not { Length: > 0 } value || !Path.IsPathFullyQualified(value) ||
                (OperatingSystem.IsWindows() && (value.StartsWith(@"\\?\") || value.StartsWith(@"\\.\"))))
            {
                throw new InvalidOperationException($"Set {setting.Path} to an absolute server folder path.");
            }

            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            if (Overlaps(path, dataDirectory) || paths.Any(other => Overlaps(path, other)))
            {
                throw new InvalidOperationException($"{setting.Path} overlaps another library folder or the server data directory.");
            }
            paths.Add(path);
        }
        return paths;
    }

    private static bool Overlaps(string first, string second) => Contains(first, second) || Contains(second, first);

    private static bool Contains(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    public static void RequireRoot(string path)
    {
        var ancestors = new Stack<DirectoryInfo>();
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
        {
            ancestors.Push(directory);
        }
        foreach (var directory in ancestors)
        {
            RequireDirectory(directory.FullName);
        }
    }

    private static void RequireDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The scan path is not a directory or points to another location.");
        }
    }

    public static long Collect(string path, Action<LibraryFileFact> addFile, CancellationToken cancellationToken)
    {
        long skippedLinks = 0;
        Visit(new DirectoryInfo(path));
        return skippedLinks;

        void Visit(DirectoryInfo directory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireDirectory(directory.FullName);
            var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };
            foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedLinks++;
                }
                else if (entry is DirectoryInfo child)
                {
                    Visit(child);
                }
                else if (entry is FileInfo file && Extensions.Contains(file.Extension))
                {
                    var relativePath = Path.GetRelativePath(path, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
                    addFile(new(relativePath, file.Extension[1..].ToLowerInvariant(), file.Length, file.LastWriteTimeUtc));
                }
            }
        }
    }
}
