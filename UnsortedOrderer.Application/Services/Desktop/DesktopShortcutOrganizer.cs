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

    internal (int Moved, int Left, int Errors) Group(string desktop, string source, string? destination, IEnumerable<string> links)
    {
        var moved = 0;
        var left = 0;
        var errors = 0;
        foreach (var link in links)
        {
            var didMove = false;
            var completed = TryShortcutOperation(link, () =>
            {
                if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0)
                    return;
                var info = store.Read(link);
                var category = info is null ? null : DesktopShortcutClassifier.GetFolder(info);
                if (category is null)
                    return;

                var folder = Path.Combine(desktop, category);
                if (ApplicationLocation.ContainsPath(folder, source) ||
                    destination is not null && ApplicationLocation.ContainsPath(folder, destination))
                    throw new IOException($"Группа совпадает с папкой сортировки: «{folder}».");
                if (File.Exists(folder) ||
                    Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Нет безопасной папки для группы «{folder}».");

                FileUtilities.MoveFile(link, folder);
                didMove = true;
                log.WriteLine($"Ярлык перенесён в {category}: «{link}».");
            });
            if (didMove)
                moved++;
            else
                left++;
            if (!completed)
                errors++;
        }
        return (moved, left, errors);
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

    private bool TryShortcutOperation(string path, Action operation)
    {
        try
        {
            operation();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException
            or ArgumentException or System.Reflection.TargetInvocationException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            log.WriteLine($"Не удалось обработать ярлык «{path}»: {ex.Message}");
            return false;
        }
    }

    internal static bool IsShortcut(string file) => IsWindowsShortcut(file) ||
        Path.GetExtension(file).Equals(".url", StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowsShortcut(string file) =>
        Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
}
