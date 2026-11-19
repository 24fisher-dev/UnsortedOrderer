using System.Buffers.Binary;

namespace UnsortedOrderer.Services.Desktop;

internal static class DesktopShortcutTargetProbe
{
    private const uint HasItemIdList = 0x1;
    private const uint HasLinkInfo = 0x2;
    private const uint HasRelativePath = 0x8;
    private const uint HasEnvironmentTarget = 0x200;
    private const uint HasDarwinId = 0x1000;
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-c000-000000000046");

    public static bool IsBrokenLocalLink(string link, DesktopShortcut shortcut)
    {
        // An empty WScript target is not enough: virtual shell and advertised installer
        // shortcuts may still have a usable item ID, relative path or special descriptor.
        using (var stream = File.OpenRead(link))
        {
            Span<byte> header = stackalloc byte[0x4c];
            if (stream.Length < header.Length + sizeof(uint))
                return false;
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != header.Length ||
                new Guid(header[4..20]) != ShellLinkClassId)
                return false;

            var flags = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
            if ((flags & (HasEnvironmentTarget | HasDarwinId)) != 0)
                return false;
            if (string.IsNullOrWhiteSpace(shortcut.Target))
                return string.IsNullOrWhiteSpace(shortcut.Arguments) &&
                    (flags & (HasItemIdList | HasLinkInfo | HasRelativePath)) == 0;
        }

        var target = shortcut.Target.Trim();
        if (!Path.IsPathFullyQualified(target) || target.Length < 3 || target[1] != ':' || target.Contains('%'))
            return false;

        var drive = new DriveInfo(Path.GetPathRoot(target)!);
        if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
            return false;

        // Leave shortcuts through junctions or cloud placeholders untouched. Inaccessible paths
        // throw instead of being reported as missing, so the caller logs and keeps the shortcut.
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        for (var item = target; item is not null; item = Path.GetDirectoryName(item))
        {
            try
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || item == target)
                    return false;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

        return true;
    }
}
