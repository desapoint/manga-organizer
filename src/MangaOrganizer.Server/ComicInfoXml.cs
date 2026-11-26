using System.Xml;
using System.Xml.Linq;

namespace MangaOrganizer.Server;

internal static class ComicInfoXml
{
    public static XElement? Read(ComicArchive archive)
    {
        const int maxMetadataBytes = 256 * 1024;
        var metadata = archive.Entries.Where(entry => entry.Name.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName.Contains('/')).FirstOrDefault();
        if (metadata is null || metadata.Length is <= 0 or > maxMetadataBytes) return null;

        try
        {
            using var stream = metadata.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = maxMetadataBytes,
            });
            var info = XDocument.Load(reader).Root;
            return info?.Name.LocalName == "ComicInfo" ? info : null;
        }
        catch (Exception error) when (error is XmlException || ArchivePages.IsUnreadable(error))
        {
            return null;
        }
    }
}
