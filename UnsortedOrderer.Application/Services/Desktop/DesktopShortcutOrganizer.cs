using System.Runtime.InteropServices;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;

namespace UnsortedOrderer.Services.Desktop;

public sealed class DesktopShortcutOrganizer(IDesktopShortcutStore store, IMessageWriter log)
{
    internal static bool IsGroupFolder(string name) =>
        name.Equals(DesktopShortcutClassifier.GamesFolder, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(DesktopShortcutClassifier.EditorsFolder, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(DesktopShortcutClassifier.UtilitiesFolder, StringComparison.OrdinalIgnoreCase);

    public void Group(string desktop, string source, string? destination)
    {
        foreach (var link in Directory.EnumerateFiles(desktop).Where(IsShortcut).ToArray())
        {
            TryShortcutOperation(link, () =>
            {
                if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0)
                    return;
                var info = store.Read(link);
                var category = info is null ? null : DesktopShortcutClassifier.GetFolder(info);
                if (category is null)
                    return;

                var folder = Path.Combine(desktop, category);
                if (ApplicationLocation.ContainsPath(source, folder) ||
                    destination is not null && ApplicationLocation.SamePath(destination, folder) ||
                    File.Exists(folder) ||
                    Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                    return;
                FileUtilities.MoveFile(link, folder);
                log.WriteLine($"Desktop shortcut moved to {category}: {Path.GetFileName(link)}");
            });
        }
    }

    public void EnsureFolderLink(string desktop, string name, string target)
    {
        if (!Directory.Exists(target))
            return;

        target = Path.GetFullPath(target);
        foreach (var file in Directory.EnumerateFiles(desktop).Where(IsWindowsShortcut))
        {
            var found = false;
            TryShortcutOperation(file, () =>
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    return;
                var info = store.Read(file);
                found = info is not null && string.IsNullOrEmpty(info.Arguments) &&
                    Path.IsPathFullyQualified(info.Target) && ApplicationLocation.SamePath(info.Target, target);
            });
            if (found)
                return;
        }

        var link = Path.Combine(desktop, name + ".lnk");
        for (var n = 1; File.Exists(link) || Directory.Exists(link); n++)
            link = Path.Combine(desktop, $"{name} ({n}).lnk");

        TryShortcutOperation(link, () => store.CreateFolderLink(link, target));
    }

    private void TryShortcutOperation(string path, Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException
            or ArgumentException or System.Reflection.TargetInvocationException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            log.WriteLine($"Desktop shortcut skipped: {Path.GetFileName(path)} ({ex.Message})");
        }
    }

    internal static bool IsShortcut(string file) => IsWindowsShortcut(file) ||
        Path.GetExtension(file).Equals(".url", StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowsShortcut(string file) =>
        Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
}
