using System.Reflection.PortableExecutable;
using System.Xml;
using UnsortedOrderer.Categories;
using UnsortedOrderer.Contracts.Categories;

namespace UnsortedOrderer.Application.Services.Categories;

internal enum GpxFormat { Unknown, GuitarPro, Navigation }

internal static class FileFormatProbe
{
    public static ICategory? ResolveGpxCategory(string path, IEnumerable<ICategory> categories) =>
        ReadGpx(path) switch
        {
            GpxFormat.GuitarPro => categories.OfType<MusicalInstrumentsCategory>().FirstOrDefault(),
            GpxFormat.Navigation => categories.OfType<GpsTracksCategory>().FirstOrDefault(),
            _ => null
        };

    private static GpxFormat ReadGpx(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[4];
            var bytesRead = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
            if (bytesRead < signature.Length || stream.Length == signature.Length)
            {
                return GpxFormat.Unknown;
            }

            if (signature.SequenceEqual("BCFS"u8) || signature.SequenceEqual("BCFZ"u8))
            {
                return GpxFormat.GuitarPro;
            }

            stream.Position = 0;
            using var xml = OpenXml(stream);
            if (xml.MoveToContent() != XmlNodeType.Element || xml.LocalName != "gpx")
            {
                return GpxFormat.Unknown;
            }

            if (xml.NamespaceURI is "http://www.topografix.com/GPX/1/0" or "http://www.topografix.com/GPX/1/1")
            {
                return GpxFormat.Navigation;
            }

            if (xml.NamespaceURI.Length == 0 && !xml.IsEmptyElement)
            {
                while (xml.Read())
                {
                    if (xml.Depth == 1 && xml.NodeType == XmlNodeType.Element &&
                        xml.NamespaceURI.Length == 0 && xml.LocalName is "trk" or "rte" or "wpt")
                    {
                        return GpxFormat.Navigation;
                    }
                }
            }

            return GpxFormat.Unknown;
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return GpxFormat.Unknown;
        }
    }

    public static bool IsDataXml(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var xml = OpenXml(stream);
            return xml.MoveToContent() == XmlNodeType.Element
                && xml.LocalName.ToLowerInvariant() is not ("configuration" or "config" or "settings" or
                    "manifest" or "assembly" or "project" or "packages" or "gpx" or "svg" or "score-partwise" or "score-timewise");
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsWindowsExecutable(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            return pe.PEHeaders.PEHeader is not null;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static XmlReader OpenXml(Stream stream) => XmlReader.Create(stream, new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 128 * 1024
    });
}
