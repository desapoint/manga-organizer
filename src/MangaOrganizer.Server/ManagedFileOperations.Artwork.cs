using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MangaOrganizer.Server;

internal sealed partial class ManagedFileOperations
{
    private static FileInfo FindVariantTarget(FileInfo existing)
    {
        var stem = Path.GetFileNameWithoutExtension(existing.Name);
        for (var number = 1; number <= 9999; number++)
        {
            var suffix = $" [Variant {number.ToString(CultureInfo.InvariantCulture)}]{existing.Extension}";
            var prefix = stem[..Math.Min(stem.Length, 255 - suffix.Length)];
            while (prefix.Length > 0 && (char.IsHighSurrogate(prefix[^1]) ||
                (!OperatingSystem.IsWindows() && Encoding.UTF8.GetByteCount(prefix + suffix) > 255)))
            {
                prefix = prefix[..^1];
            }
            var candidate = new FileInfo(Path.Combine(existing.DirectoryName!, prefix.TrimEnd() + suffix));
            if (!PathExists(candidate.FullName)) return candidate;
        }
        throw new FileOperationException("Could not find an unused variant name. Choose another destination path.");
    }

    private static FileOperationCover[] PlanCovers(FileOperation operation, DirectoryInfo source, DirectoryInfo target)
    {
        var existing = CoverArt.Sidecars(target);
        var occupied = existing.Select(file => CoverArt.SidecarNumber(file.Name)!.Value).ToHashSet();
        var knownImages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in existing)
        {
            if (ReadCoverDigest(file) is { } digest) knownImages.Add(Convert.ToHexString(digest));
        }

        var planned = new List<FileOperationCover>();
        var number = 0;
        foreach (var original in CoverArt.Sidecars(source))
        {
            if (ReadCoverDigest(original) is not { } digest || !knownImages.Add(Convert.ToHexString(digest))) continue;

            FileInfo destination;
            do
            {
                var stem = number == 0 ? "cover" : "cover-" + number.ToString(CultureInfo.InvariantCulture);
                destination = new(Path.Combine(target.FullName, stem + original.Extension.ToLowerInvariant()));
            }
            while (occupied.Contains(number++) || PathExists(destination.FullName));

            planned.Add(new(Relative(operation.SourceBase, original), Relative(operation.TargetBase, destination), original.Length, digest));
        }
        return planned.ToArray();
    }

    private void ValidateCovers(FileOperation operation)
    {
        if (operation.Covers.Length == 0) return;
        if (store.HasOtherSeriesInFolder(operation.SourceRoot, operation.SourcePath, operation.SeriesKey))
        {
            throw new FileOperationException("Artwork ownership changed after preview. Review the source folder before trying again.");
        }
        foreach (var cover in operation.Covers)
        {
            var (source, target) = ResolveCover(operation, cover);
            if (PathExists(target.FullName) || !Matches(source, cover.Size, cover.Sha256))
            {
                throw new FileOperationException("A planned cover changed or its destination is occupied. Create a new preview.");
            }
        }
    }

    private void CopyCovers(FileOperation operation)
    {
        var covers = operation.Covers;
        for (var index = 0; index < covers.Length; index++)
        {
            var cover = covers[index];
            var (source, target) = ResolveCover(operation, cover);
            var staged = StagingPath(target, operation.Id, "cover-" + index.ToString(CultureInfo.InvariantCulture));
            if (!CopyVerified(source, staged, cover.Size, cover.Sha256))
            {
                throw new FileOperationException("Could not make a verified copy of the cover. Its original was kept.");
            }
            LibraryScanner.RequireRoot(target.DirectoryName!);
            File.Move(staged.FullName, target.FullName, overwrite: false);
            if (!Matches(target, cover.Size, cover.Sha256))
            {
                throw new FileOperationException("The copied cover changed during verification. Its original was kept.");
            }
            covers[index] = cover with { Published = true };
            store.RecordCoverPublication(operation.Id, covers);
        }
    }

    private static byte[]? ReadCoverDigest(FileInfo file)
    {
        file.Refresh();
        if (!file.Exists || file.Length > RasterImage.MaxCoverBytes) return null;
        var size = file.Length;
        return MediaFiles.Read(file, size, file.LastWriteTimeUtc, stream =>
            RasterImage.Read(stream, size, RasterImage.MaxCoverBytes) is { } image ? SHA256.HashData(image.Bytes) : null);
    }

    private static (FileInfo Source, FileInfo Target) ResolveCover(FileOperation operation, FileOperationCover cover) =>
        (MediaFiles.Resolve(operation.SourceBase, cover.SourcePath) ?? throw new FileOperationException("The cover source is invalid."),
            MediaFiles.Resolve(operation.TargetBase, cover.TargetPath) ?? throw new FileOperationException("The cover destination is invalid."));

    private static string Relative(string root, FileInfo file) =>
        Path.GetRelativePath(root, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
}
