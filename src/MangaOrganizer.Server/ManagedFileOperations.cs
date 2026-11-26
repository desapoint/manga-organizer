using System.Text.RegularExpressions;

namespace MangaOrganizer.Server;

internal sealed record FileOperationRequest(Guid SourceRoot, string? FileId, Guid TargetRoot, string? TargetPath,
    bool KeepBoth = false, bool CopyFolderCovers = false);
internal sealed record FileOperationCoverView(string Source, string Target, bool Published);
internal sealed record FileOperationView(Guid Id, string State, string Source, string Target, long Size, string Sha256,
    string? Recovery, string? Message, IReadOnlyList<FileOperationCoverView> Covers);
internal sealed class FileOperationException(string message) : Exception(message);

internal sealed partial class ManagedFileOperations(LibraryStore store, string dataDirectory) : IDisposable
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly SemaphoreSlim gate = new(1);

    public FileOperationView Preview(FileOperationRequest request)
    {
        if (request.FileId is not { Length: 32 } id || !id.All(char.IsAsciiHexDigit))
        {
            throw new FileOperationException("Choose an indexed file with a verified hash.");
        }

        var file = store.ReadManagedFile(request.SourceRoot, id.ToLowerInvariant());
        var sourceRoot = store.ReadManagedRoot(request.SourceRoot);
        var targetRoot = store.ReadManagedRoot(request.TargetRoot);
        if (file is null || sourceRoot is null || targetRoot is null)
        {
            throw new FileOperationException("Enable managed access for both folders and scan the source file first.");
        }

        var targetPath = NormalizeTargetPath(request.TargetPath, file.RelativePath);
        var operation = new FileOperation(Guid.NewGuid(), "planned", request.SourceRoot, request.TargetRoot,
            sourceRoot, targetRoot, file.RelativePath, targetPath, file.Id, file.ContentId, file.Size, file.Sha256,
            file.SeriesKey, file.SeriesTitle, file.Volume, file.Chapter, null, []);
        ValidateRoots(operation);
        var source = Source(operation);
        var target = Target(operation);
        if (string.Equals(source.FullName, target.FullName, StringComparison.Ordinal))
        {
            throw new FileOperationException("Choose a different destination path.");
        }
        if (!source.Exists || source.Length != operation.Size)
        {
            throw new FileOperationException("The indexed source has changed. Rescan it first.");
        }
        ValidateTargetParents(target, operation.TargetBase);
        if (!PathsAlias(source, target) && PathExists(target.FullName))
        {
            if (!request.KeepBoth) throw new FileOperationException("The destination already exists. Choose Keep both to preview a numbered destination.");
            target = FindVariantTarget(target);
            operation = operation with { TargetPath = Relative(operation.TargetBase, target) };
        }

        if (request.CopyFolderCovers && !string.Equals(source.DirectoryName, target.DirectoryName, PathComparison))
        {
            if (store.HasOtherSeriesInFolder(operation.SourceRoot, operation.SourcePath, operation.SeriesKey))
            {
                throw new FileOperationException("This source folder contains several series. Clear Copy folder covers to move this file on its own.");
            }
            operation = operation with { Covers = PlanCovers(operation, source.Directory!, target.Directory!) };
        }
        store.RecordFileOperation(operation);
        return View(operation);
    }

    public IReadOnlyList<FileOperationView> History() => store.ReadFileOperations().Select(View).ToArray();

    public async Task<FileOperationView?> Run(Guid id, bool restore, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var operation = store.ReadFileOperation(id);
            if (operation is null) return null;
            if (operation.State == (restore ? "restored" : "completed")) return View(operation);
            if (!store.BeginFileOperation(id, restore))
            {
                throw new FileOperationException("The folders or operation changed, or a scan is still running.");
            }

            try
            {
                ValidateRoots(operation);
                if (restore) Restore(operation);
                else Apply(operation);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
                Microsoft.Data.Sqlite.SqliteException or InvalidOperationException or FileOperationException)
            {
                var message = error is FileOperationException operationError ? operationError.Message :
                    "The operation could not finish. Retained files remain available for review and recovery.";
                store.ReviewFileOperation(operation.Id, message);
                throw new FileOperationException(message);
            }

            return store.ReadFileOperation(operation.Id) is { } finished ? View(finished) : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    private void Apply(FileOperation operation)
    {
        var original = Source(operation);
        var target = Target(operation);
        if (!PathsAlias(original, target) && PathExists(target.FullName))
        {
            throw new FileOperationException("The destination already exists. The source stays at its original path.");
        }
        var current = store.ReadManagedFile(operation.SourceRoot, operation.FileId);
        if (current is null || current.ContentId != operation.ContentId || current.RelativePath != operation.SourcePath)
        {
            throw new FileOperationException("The indexed source changed after preview. Create a new plan after rescanning.");
        }
        ValidateCovers(operation);

        var backup = Backup(operation);
        if (!PathExists(backup.FullName))
        {
            var pending = new FileInfo(backup.FullName + ".part");
            if (!CopyVerified(original, pending, operation))
            {
                throw new FileOperationException("Could not create and verify a recovery copy. The source stays at its original path.");
            }
            File.Move(pending.FullName, backup.FullName, overwrite: false);
        }
        if (!Matches(backup, operation))
        {
            throw new FileOperationException("Could not create and verify a recovery copy. The source stays at its original path.");
        }

        PrepareDirectory(target, operation.TargetBase);
        var staging = StagingPath(target, operation.Id, "apply");
        if (!CopyVerified(backup, staging, operation))
        {
            throw new FileOperationException("The destination could not accept a verified copy. The source stays at its original path.");
        }

        if (!Matches(original, operation))
        {
            throw new FileOperationException("The source changed during the operation. Verified recovery and staging copies were retained.");
        }

        CopyCovers(operation);

        var parked = ParkPath(original, operation.Id, "original");
        LibraryScanner.RequireRoot(original.DirectoryName!);
        File.Move(original.FullName, parked.FullName, overwrite: false);
        if (!Matches(parked, operation))
        {
            if (!PathExists(original.FullName)) File.Move(parked.FullName, original.FullName, overwrite: false);
            throw new FileOperationException("The file moved during revalidation. The original and recovery copies were retained for review.");
        }

        LibraryScanner.RequireRoot(target.DirectoryName!);
        File.Move(staging.FullName, target.FullName, overwrite: false);
        if (!Matches(target, operation))
        {
            throw new FileOperationException("The published file changed during verification. Restore the original from the journal.");
        }

        Finish(operation, target, restore: false, retainTarget: false);
    }

    private void Restore(FileOperation operation)
    {
        var original = Source(operation);
        var target = Target(operation);
        var samePathOnDisk = PathsAlias(original, target);
        var backup = Backup(operation);
        var sourcePark = ParkPath(original, operation.Id, "original");
        var usableBackup = Matches(backup, operation) ? backup : Matches(sourcePark, operation) ? sourcePark : null;
        var removeTarget = operation.State == "completed" && Matches(target, operation);
        if (samePathOnDisk && !removeTarget && PathExists(original.FullName))
        {
            throw new FileOperationException("The existing file blocks a case-only restore. Check the journal before retrying.");
        }

        var needsCopy = samePathOnDisk || !PathExists(original.FullName);
        if (!needsCopy && !Matches(original, operation))
        {
            throw new FileOperationException("The original path is occupied by different content.");
        }

        if (needsCopy)
        {
            if (usableBackup is null) throw new FileOperationException("No verified recovery copy is available.");
            PrepareDirectory(original, operation.SourceBase);
            var staging = StagingPath(original, operation.Id, "restore");
            if (PathExists(staging.FullName) ? !Matches(staging, operation) : !CopyVerified(usableBackup, staging, operation))
            {
                throw new FileOperationException("Could not prepare a verified restoration.");
            }

            if (samePathOnDisk && removeTarget) ParkTarget(target, operation);
            LibraryScanner.RequireRoot(original.DirectoryName!);
            File.Move(staging.FullName, original.FullName, overwrite: false);
        }
        if (!Matches(original, operation)) throw new FileOperationException("The original path changed during restoration.");

        if (!samePathOnDisk && removeTarget) ParkTarget(target, operation);
        var remainingTarget = !samePathOnDisk && PathExists(target.FullName);
        Finish(operation, original, restore: true, remainingTarget);
    }

    private void Finish(FileOperation operation, FileInfo file, bool restore, bool retainTarget)
    {
        file.Refresh();
        DateTimeOffset modified = file.LastWriteTimeUtc;
        store.FinishFileOperation(operation, restore, modified, retainTarget);
    }

    private static void ParkTarget(FileInfo target, FileOperation operation)
    {
        if (!Matches(target, operation)) return;
        LibraryScanner.RequireRoot(target.DirectoryName!);
        var parked = ParkPath(target, operation.Id, "restored-destination");
        File.Move(target.FullName, parked.FullName, overwrite: false);
        if (!Matches(parked, operation))
        {
            if (!PathExists(target.FullName)) File.Move(parked.FullName, target.FullName, overwrite: false);
            throw new FileOperationException("The destination changed during restore. Check the retained files.");
        }
    }

    private static bool CopyVerified(FileInfo source, FileInfo target, FileOperation operation) =>
        CopyVerified(source, target, operation.Size, operation.Sha256);

    private static bool CopyVerified(FileInfo source, FileInfo target, long size, byte[] digest)
    {
        source.Refresh();
        if (!source.Exists || source.Length != size) return false;
        LibraryScanner.RequireRoot(target.DirectoryName!);

        var copied = MediaFiles.Read(source, size, source.LastWriteTimeUtc, input =>
        {
            using var output = new FileStream(target.FullName, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
            output.Position = 0;
            return MediaFiles.Hash(output, CancellationToken.None);
        });
        return copied is not null && copied.AsSpan().SequenceEqual(digest) && Matches(target, size, digest);
    }

    private static bool Matches(FileInfo file, FileOperation operation) => Matches(file, operation.Size, operation.Sha256);

    private static bool Matches(FileInfo file, long size, byte[] digest)
    {
        file.Refresh();
        if (!file.Exists || file.Length != size) return false;
        var hash = MediaFiles.Read(file, size, file.LastWriteTimeUtc, stream => MediaFiles.Hash(stream, CancellationToken.None));
        return hash is not null && hash.AsSpan().SequenceEqual(digest);
    }

    private FileInfo Backup(FileOperation operation)
    {
        LibraryScanner.RequireRoot(dataDirectory);
        var directory = Path.Combine(dataDirectory, "file-recovery");
        Directory.CreateDirectory(directory);
        LibraryScanner.RequireRoot(directory);
        return new FileInfo(Path.Combine(directory, operation.Id.ToString("N") + ".backup"));
    }

    private FileOperationView View(FileOperation operation)
    {
        var recovery = Path.Combine(dataDirectory, "file-recovery", operation.Id.ToString("N") + ".backup");
        return new(operation.Id, operation.State, Source(operation).FullName, Target(operation).FullName, operation.Size,
            Convert.ToHexStringLower(operation.Sha256), File.Exists(recovery) ? recovery : null, operation.Message,
            operation.Covers.Select(cover => new FileOperationCoverView(Path.Combine(operation.SourceBase, cover.SourcePath),
                Path.Combine(operation.TargetBase, cover.TargetPath), cover.Published)).ToArray());
    }

    private static FileInfo Source(FileOperation operation) =>
        MediaFiles.Resolve(operation.SourceBase, operation.SourcePath) ?? throw new FileOperationException("The source path is invalid.");
    private static FileInfo Target(FileOperation operation) =>
        MediaFiles.Resolve(operation.TargetBase, operation.TargetPath) ?? throw new FileOperationException("The destination path is invalid.");
    private static FileInfo ParkPath(FileInfo file, Guid id, string suffix) =>
        new(Path.Combine(file.DirectoryName!, $".manga-organizer-{id:N}-{suffix}.recovery"));
    private static FileInfo StagingPath(FileInfo file, Guid id, string suffix) =>
        new(Path.Combine(file.DirectoryName!, $".manga-organizer-{id:N}-{suffix}.part"));
    private static bool PathsAlias(FileInfo source, FileInfo target) => string.Equals(source.FullName, target.FullName, PathComparison);

    private static void ValidateRoots(FileOperation operation)
    {
        LibraryScanner.RequireRoot(operation.SourceBase);
        LibraryScanner.RequireRoot(operation.TargetBase);
    }

    private static bool PathExists(string path)
    {
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string NormalizeTargetPath(string? value, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096)
        {
            throw new FileOperationException("Enter a destination path relative to the selected folder.");
        }

        var path = value.Trim().Replace('\\', '/');
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var part in path.Split('/'))
        {
            if (part.Length is 0 or > 255 || part is "." or ".." || part.Contains(':') || part.IndexOfAny(invalid) >= 0 ||
                part.StartsWith(".manga-organizer-", StringComparison.OrdinalIgnoreCase) ||
                OperatingSystem.IsWindows() && (part.EndsWith('.') || part.EndsWith(' ') || DeviceName().IsMatch(part)))
            {
                throw new FileOperationException("Enter a normal file path inside the destination folder.");
            }
        }
        if (!string.Equals(Path.GetExtension(path), Path.GetExtension(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new FileOperationException("Keep the file's existing extension.");
        }
        return path;
    }

    private static void ValidateTargetParents(FileInfo file, string root)
    {
        var ancestor = file.Directory;
        while (ancestor is not null && !ancestor.Exists)
        {
            if (PathExists(ancestor.FullName) || !LibraryScanner.Contains(root, ancestor.FullName))
            {
                throw new FileOperationException("The destination parent is not an available directory.");
            }
            ancestor = ancestor.Parent;
        }
        if (ancestor is null) throw new FileOperationException("The destination is unavailable.");
        LibraryScanner.RequireRoot(ancestor.FullName);
    }

    private static void PrepareDirectory(FileInfo file, string root)
    {
        ValidateTargetParents(file, root);
        Directory.CreateDirectory(file.DirectoryName!);
        LibraryScanner.RequireRoot(file.DirectoryName!);
    }

    [GeneratedRegex(@"^(?:CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeviceName();
}
