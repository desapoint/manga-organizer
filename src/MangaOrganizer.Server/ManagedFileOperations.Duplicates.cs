namespace MangaOrganizer.Server;

internal sealed record DuplicateFileView(Guid RootId, string FileId, string RelativePath, string FullPath, string Format,
    long Size, string Sha256, string? Volume, bool VolumeArchive, IReadOnlyList<SeriesChapter> Chapters, bool HasCover, string? SeriesCoverKey);
internal sealed record DuplicateComparison(DuplicateFileView Candidate, string Kind, bool CanQuarantine, string Reason,
    IReadOnlyList<SeriesChapter> Shared, IReadOnlyList<SeriesChapter> SourceOnly, IReadOnlyList<SeriesChapter> CandidateOnly);
internal sealed record DuplicateReview(DuplicateFileView Source, IReadOnlyList<DuplicateComparison> Comparisons);
internal sealed record QuarantineRequest(Guid RootId, string? SourceFile, string? KeepFile);

internal sealed partial class ManagedFileOperations
{
    internal const string QuarantineDirectory = ".manga-organizer-quarantine";

    public DuplicateReview? ReviewDuplicates(Guid root, string file)
    {
        var source = store.ReadDuplicateFile(root, file);
        if (source is null) return null;
        var identity = Identity(source);
        var comparisons = store.ReadDuplicateCandidates(source).Select(candidate => Compare(source, candidate, identity))
            .Where(item => item.Kind != "unrelated").ToArray();
        return new(FileView(source), comparisons);
    }

    public FileOperationView PreviewQuarantine(Guid root, string sourceId, string keepId)
    {
        var (source, keep) = QuarantineFiles(root, sourceId, keepId);
        VerifyExactCopy(source, keep);
        var id = Guid.NewGuid();
        var targetPath = QuarantineDirectory + "/" + id.ToString("N") + ".recovery";
        var operation = new FileOperation(id, "planned", root, root, source.RootPath, source.RootPath,
            source.RelativePath, targetPath, source.FileId, source.ContentId, source.Size, source.Sha256, source.SeriesKey,
            source.SeriesTitle, source.VolumeHint, source.ChapterHint, null, [], keep.FileId, keep.RelativePath);
        ValidateRoots(operation);
        ValidateTargetParents(Target(operation), operation.TargetBase);
        store.RecordFileOperation(operation);
        return View(operation);
    }

    private (DuplicateFile Source, DuplicateFile Keep) QuarantineFiles(Guid root, string sourceId, string keepId)
    {
        var source = store.ReadDuplicateFile(root, sourceId);
        var keep = store.ReadDuplicateFile(root, keepId);
        if (source is null || keep is null || source.FileId == keep.FileId)
            throw new FileOperationException("The original and retained files must both be present in the managed folder.");
        return (source, keep);
    }

    private void VerifyQuarantine(FileOperation operation)
    {
        if (operation.QuarantineKeepFile is not { } keepFile) return;
        var (source, keep) = QuarantineFiles(operation.SourceRoot, operation.FileId, keepFile);
        if (source.ContentId != operation.ContentId || source.RelativePath != operation.SourcePath ||
            keep.RelativePath != operation.QuarantineKeepPath)
            throw new FileOperationException("The selected files changed since preview. Review the pair again.");
        VerifyExactCopy(source, keep);
    }

    private static void VerifyExactCopy(DuplicateFile source, DuplicateFile keep)
    {
        var comparison = Compare(source, keep, Identity(source));
        if (!comparison.CanQuarantine) throw new FileOperationException(comparison.Reason);
        var retained = MediaFiles.Resolve(keep.RootPath, keep.RelativePath);
        var original = MediaFiles.Resolve(source.RootPath, source.RelativePath);
        if (retained is null || original is null || !Matches(retained, keep.Size, keep.Sha256) ||
            !Matches(original, source.Size, source.Sha256))
            throw new FileOperationException("A file no longer matches its indexed hash. Rescan the folder and review the pair again.");
    }

    private static PhysicalFile? Identity(DuplicateFile item) => MediaFiles.Resolve(item.RootPath, item.RelativePath) is { } file
        ? RegularFile.Identity(file, item.Size, item.ModifiedAt.UtcDateTime) : null;

    private static DuplicateComparison Compare(DuplicateFile source, DuplicateFile candidate, PhysicalFile? sourceIdentity)
    {
        var exact = source.Size == candidate.Size && source.Sha256.AsSpan().SequenceEqual(candidate.Sha256);
        var sourceKeys = source.Coverage.Select(item => (item.Volume, item.Chapter)).ToHashSet();
        var candidateKeys = candidate.Coverage.Select(item => (item.Volume, item.Chapter)).ToHashSet();
        var sourceOnly = source.Coverage.Where(item => !candidateKeys.Contains((item.Volume, item.Chapter))).ToArray();
        var candidateOnly = candidate.Coverage.Where(item => !sourceKeys.Contains((item.Volume, item.Chapter))).ToArray();
        var shared = source.Coverage.Where(item => candidateKeys.Contains((item.Volume, item.Chapter))).ToArray();
        var possible = !exact && source.Coverage.Any(item => candidate.Coverage.Any(other =>
            item.Chapter == other.Chapter && (item.Volume is null || other.Volume is null)));
        var kind = exact ? "exact" : shared.Length > 0 ? (sourceOnly.Length == 0 && candidateOnly.Length == 0 ? "alternate" : "overlap")
            : possible ? "possible-overlap" : "unrelated";
        var allowed = false;
        var reason = kind == "possible-overlap"
            ? "The chapter label matches, but its volume or edition is unresolved. Keep both files."
            : "Different bytes can contain different pages, chapters, or translations. Keep both files.";
        if (exact)
        {
            var candidateIdentity = Identity(candidate);
            if (sourceIdentity is null || candidateIdentity is null)
            {
                kind = "identity-unknown";
                reason = "Physical file identity is unavailable. Review both paths before making other changes.";
            }
            else if (sourceIdentity.Device == candidateIdentity.Device && sourceIdentity.Index == candidateIdentity.Index)
            {
                kind = "hardlink";
                reason = "These paths link to the same physical file. Both paths are protected.";
            }
            else if (sourceIdentity.Links != 1)
            {
                reason = "The original has hardlink aliases and is protected.";
            }
            else if (!source.Managed || source.State != "complete"
                || source.RootId != candidate.RootId || source.SeriesKey != candidate.SeriesKey)
            {
                reason = "Quarantine requires two verified paths in the same scanned, managed series folder.";
            }
            else if (source.VolumeHint != candidate.VolumeHint || source.ChapterHint != candidate.ChapterHint
                || source.Format != candidate.Format || sourceOnly.Length > 0 || candidateOnly.Length > 0)
            {
                reason = "The files have different volume, chapter, or format assignments. Review both paths and keep their coverage.";
            }
            else
            {
                allowed = true;
                reason = "Both files have equal indexed hashes and the same chapter coverage. The retained copy is checked again before quarantine.";
            }
        }
        return new(FileView(candidate), kind, allowed, reason, shared, sourceOnly, candidateOnly);
    }

    private static DuplicateFileView FileView(DuplicateFile file) => new(file.RootId, file.FileId, file.RelativePath,
        Path.Combine(file.RootPath, file.RelativePath), file.Format, file.Size, Convert.ToHexStringLower(file.Sha256),
        file.VolumeHint, file.VolumeHint is not null && file.ChapterHint is null, file.Coverage, file.HasCover, file.SeriesCoverKey);
}
