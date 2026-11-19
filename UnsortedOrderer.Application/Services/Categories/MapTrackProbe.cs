using System.IO.Compression;
using System.Xml;

namespace UnsortedOrderer.Application.Services.Categories;

internal static class MapTrackProbe
{
    private const int MaxXmlSize = 16 * 1024 * 1024;
    private const string GoogleExtension = "http://www.google.com/kml/ext/2.2";

    public static bool IsCandidate(string extension) =>
        extension.Equals(".kml", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".kmz", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tcx", StringComparison.OrdinalIgnoreCase);

    public static bool IsTrack(string path)
    {
        var extension = Path.GetExtension(path);
        if (!IsCandidate(extension))
            return false;

        try
        {
            using var file = File.OpenRead(path);
            if (extension.Equals(".kmz", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = new ZipArchive(file, ZipArchiveMode.Read);
                var kml = archive.Entries.Where(entry =>
                    Path.GetExtension(entry.Name).Equals(".kml", StringComparison.OrdinalIgnoreCase)).ToArray();
                var entry = kml.FirstOrDefault(e => e.FullName.Equals("doc.kml", StringComparison.OrdinalIgnoreCase))
                    ?? (kml.Length == 1 ? kml[0] : null);
                if (entry is null || entry.Length > MaxXmlSize)
                    return false;
                using var document = entry.Open();
                return ReadKml(document);
            }

            if (file.Length > MaxXmlSize)
                return false;
            return extension.Equals(".tcx", StringComparison.OrdinalIgnoreCase) ? ReadTcx(file) : ReadKml(file);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException or UnauthorizedAccessException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static XmlReader OpenXml(Stream stream) => XmlReader.Create(stream, new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxXmlSize
    });

    private static bool ReadKml(Stream stream)
    {
        using var xml = OpenXml(stream);
        if (xml.MoveToContent() != XmlNodeType.Element || xml.LocalName != "kml" ||
            xml.NamespaceURI is not ("" or "http://www.opengis.net/kml/2.2" or
                "http://earth.google.com/kml/2.0" or "http://earth.google.com/kml/2.1" or "http://earth.google.com/kml/2.2"))
            return false;

        var kmlNamespace = xml.NamespaceURI;
        var explicitTrack = false;
        var hasLine = false;
        var hasMapGeometry = false;
        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element)
                continue;
            if (!xml.IsEmptyElement && (xml.NamespaceURI == GoogleExtension || xml.NamespaceURI == kmlNamespace)
                && xml.LocalName == "Track")
                explicitTrack = true;
            if (xml.NamespaceURI != kmlNamespace)
                continue;
            if (xml.LocalName == "LineString" && !xml.IsEmptyElement)
                hasLine = true;
            if (xml.LocalName is "Polygon" or "LinearRing" or "GroundOverlay" or "NetworkLink" or "Model")
                hasMapGeometry = true;
        }

        return explicitTrack || (hasLine && !hasMapGeometry);
    }

    private static bool ReadTcx(Stream stream)
    {
        using var xml = OpenXml(stream);
        if (xml.MoveToContent() != XmlNodeType.Element || xml.LocalName != "TrainingCenterDatabase" ||
            xml.NamespaceURI is not ("http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v1"
                or "http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2"))
            return false;
        var ns = xml.NamespaceURI;
        var trackpointDepth = -1;
        var hasPosition = false;
        while (xml.Read())
        {
            if (trackpointDepth >= 0 && xml.Depth <= trackpointDepth)
                trackpointDepth = -1;
            if (xml.NodeType != XmlNodeType.Element || xml.IsEmptyElement || xml.NamespaceURI != ns)
                continue;
            if (xml.LocalName == "Trackpoint")
                trackpointDepth = xml.Depth;
            else if (xml.LocalName == "Position" && trackpointDepth >= 0 && xml.Depth == trackpointDepth + 1)
                hasPosition = true;
        }
        return hasPosition;
    }
}
