using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UnsortedOrderer.Services.Desktop;

public interface IDesktopShortcutStore
{
    DesktopShortcut? Read(string path);
    void CreateFolderLink(string path, string targetDirectory);
}

public sealed class DesktopShortcutStore : IDesktopShortcutStore
{
    public DesktopShortcut? Read(string path)
    {
        if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            return ReadInternetShortcut(path);
        }

        return OperatingSystem.IsWindows() ? WithWindowsLink(path, link => new DesktopShortcut(
            (string)link.TargetPath, (string)link.Arguments, (string)link.WorkingDirectory)) : null;
    }

    public void CreateFolderLink(string path, string targetDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            WithWindowsLink(path, link =>
            {
                link.TargetPath = targetDirectory;
                link.WorkingDirectory = targetDirectory;
                link.Save();
                return true;
            });
        }
    }

    private static DesktopShortcut? ReadInternetShortcut(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024)
            return null;

        var inSection = false;
        foreach (var value in File.ReadLines(path))
        {
            var line = value.Trim();
            if (line.StartsWith('['))
                inSection = line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);

            if (inSection && line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                return new DesktopShortcut(line[4..].Trim());
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static T WithWindowsLink<T>(string path, Func<dynamic, T> read)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
        object shell = Activator.CreateInstance(type)!;
        object? link = null;
        try
        {
            link = ((dynamic)shell).CreateShortcut(path);
            return read(link!);
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
                Marshal.ReleaseComObject(link);
            Marshal.ReleaseComObject(shell);
        }
    }
}
