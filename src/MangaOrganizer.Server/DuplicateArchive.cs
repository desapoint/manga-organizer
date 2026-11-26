namespace MangaOrganizer.Server;

internal sealed record ArchiveMetadataField(string Label, string Value);
internal sealed record ArchiveEvidence(int ImagePages, IReadOnlyList<ArchiveMetadataField> ComicInfo);

internal sealed partial class ManagedFileOperations
{
    private static readonly (string Element, string Label)[] ArchiveMetadataFields =
    [
        ("Series", "Series"), ("Title", "Title"), ("Volume", "Volume"), ("Number", "Number"),
        ("LanguageISO", "Language"), ("Publisher", "Publisher"), ("Imprint", "Imprint"), ("Year", "Year"),
        ("Writer", "Writer"), ("Format", "Edition format"), ("PageCount", "Declared page count"),
        ("ScanInformation", "Scan information"),
    ];

    public ArchiveEvidence? ReviewArchive(Guid root, string fileId)
    {
        var indexed = store.ReadDuplicateFile(root, fileId);
        if (indexed is null || !ArchivePages.Supports(indexed.Format) ||
            MediaFiles.Resolve(indexed.RootPath, indexed.RelativePath) is not { } file) return null;

        try
        {
            return MediaFiles.Read<ArchiveEvidence>(file, indexed.Size, indexed.ModifiedAt, stream =>
            {
                using var archive = ArchivePages.Open(stream);
                if (archive is null) return null;
                var info = ComicInfoXml.Read(archive);
                var fields = new List<ArchiveMetadataField>();
                if (info is not null)
                {
                    foreach (var (element, label) in ArchiveMetadataFields)
                    {
                        var value = info.Element(info.Name.Namespace + element)?.Value.Trim();
                        if (value is { Length: > 0 }) fields.Add(new(label, value.Length > 300 ? value[..300] + "…" : value));
                    }
                }
                return new(ArchivePages.Entries(archive).Count(), fields);
            });
        }
        catch (Exception error) when (ArchivePages.IsUnreadable(error))
        {
            return null;
        }
    }
}
