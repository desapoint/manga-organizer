namespace MangaOrganizer.Server;

internal sealed record ServerFolder(string Name, string Path);
internal sealed record FolderListing(string? Path, string? Parent, bool Selectable,
    IReadOnlyList<ServerFolder> Folders, IReadOnlyList<ServerFolder> Suggestions, IReadOnlyList<ServerFolder> Breadcrumbs);

internal static class FolderBrowser
{
    public static IResult Browse(string? path, string dataDirectory)
    {
        try
        {
            return Results.Ok(string.IsNullOrEmpty(path) ? BrowseDisks(dataDirectory) : BrowseFolder(path, dataDirectory));
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { message = error.Message });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Results.BadRequest(new { message = "Manga Organizer cannot open that folder on the server." });
        }
    }

    private static FolderListing BrowseDisks(string dataDirectory)
    {
        var disks = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network)
            .Select(drive => new ServerFolder(drive.Name, drive.RootDirectory.FullName));
        var suggestions = new List<ServerFolder>
        {
            new("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            new("Home folder", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        };
        if (!OperatingSystem.IsWindows())
        {
            disks = disks.Prepend(new ServerFolder("Server filesystem", "/"));
            suggestions.AddRange([
                new("Media", "/media"), new("Data", "/data"), new("Mounts", "/mnt"),
                new("Unraid shares", "/mnt/user"),
            ]);
        }
        var locations = SortLocations(disks.Where(disk => !IsHidden(disk.Path, dataDirectory)));
        return new(null, null, false, locations, Accessible(suggestions, dataDirectory), []);
    }

    private static FolderListing BrowseFolder(string path, string dataDirectory)
    {
        var fullPath = LibraryScanner.NormalizePath(path);
        if (IsHidden(fullPath, dataDirectory))
        {
            throw new ArgumentException("Choose a folder outside the server data and system directories.");
        }
        LibraryScanner.RequireRoot(fullPath);
        var directory = new DirectoryInfo(fullPath);
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        var folders = directory.EnumerateDirectories("*", options)
            .Where(folder => !IsHidden(folder.FullName, dataDirectory))
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .Select(folder => new ServerFolder(folder.Name, folder.FullName)).ToArray();
        var breadcrumbs = new Stack<ServerFolder>();
        for (DirectoryInfo? target = directory; target is not null; target = target.Parent)
        {
            breadcrumbs.Push(new ServerFolder(target.Name, target.FullName));
        }
        return new(directory.FullName, directory.Parent?.FullName, !LibraryScanner.Contains(fullPath, dataDirectory), folders, [], breadcrumbs.ToArray());
    }

    private static ServerFolder[] Accessible(IEnumerable<ServerFolder> candidates, string dataDirectory) => SortLocations(candidates
        .Where(folder => Directory.Exists(folder.Path) && !IsHidden(folder.Path, dataDirectory))
        .Where(folder => CanOpen(folder.Path)));

    private static ServerFolder[] SortLocations(IEnumerable<ServerFolder> candidates) => candidates
        .DistinctBy(folder => folder.Path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool CanOpen(string path)
    {
        try
        {
            LibraryScanner.RequireRoot(path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsHidden(string path, string dataDirectory) => LibraryScanner.Contains(dataDirectory, path) ||
        (!OperatingSystem.IsWindows() &&
            (LibraryScanner.Contains("/proc", path) || LibraryScanner.Contains("/sys", path) || LibraryScanner.Contains("/dev", path)));
}
