using System.Runtime.InteropServices;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;

namespace UnsortedOrderer.Services.Desktop;

public sealed class DesktopShortcutOrganizer(
    IDesktopShortcutStore store, IMessageWriter log, Func<string, string, string>? moveFile = null)
{
    private readonly Func<string, string, string> _moveFile = moveFile ?? FileUtilities.MoveFile;
    internal static bool IsGroupFolder(string name) => DesktopShortcutGroupFolder.ParseCategory(name) is not null;

    private static bool IsLegacyGroupFolder(string name) =>
        name.Equals(DesktopShortcutClassifier.GamesFolder, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(DesktopShortcutClassifier.EditorsFolder, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(DesktopShortcutClassifier.UtilitiesFolder, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(DesktopShortcutClassifier.MusicFolder, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(DesktopShortcutClassifier.VrFolder, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(DesktopShortcutClassifier.DevelopmentFolder, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(DesktopShortcutClassifier.BrowsersFolder, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(DesktopShortcutClassifier.CommunicationFolder, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(DesktopShortcutClassifier.PrintingFolder, StringComparison.OrdinalIgnoreCase);

    public (int Moved, int Quarantined, int Left, int Errors) Group(string desktop, string source, string? destination, IEnumerable<string> links, string? targetDesktop = null)
    {
        var moved = 0;
        var quarantined = 0;
        var left = 0;
        var errors = 0;
        foreach (var link in links)
        {
            var didMove = false;
            var movedToTrash = false;
            var completed = TryShortcutOperation(link, () =>
            {
                if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0)
                    return;
                var info = store.Read(link);
                if (info is null)
                    return;
                if (store.IsBroken(link, info))
                {
                    if (destination is not null)
                    {
                        var trash = new DeletionQuarantine(destination).MoveFile(link);
                        movedToTrash = true;
                        log.WriteLine($"Битый ярлык перенесён в корзину: «{link}» → «{trash}».");
                    }
                    return;
                }

                var category = DesktopShortcutClassifier.GetFolder(info);
                if (category is null)
                    return;

                var folder = Path.Combine(targetDesktop ?? desktop, category);
                if (ApplicationLocation.ContainsPath(folder, source) ||
                    destination is not null && ApplicationLocation.ContainsPath(folder, destination))
                    throw new IOException($"Группа совпадает с папкой сортировки: «{folder}».");
                if (File.Exists(folder) ||
                    Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Нет безопасной папки для группы «{folder}».");

                _moveFile(link, folder);
                didMove = true;
                log.WriteLine($"Ярлык перенесён в {category}: «{link}».");
            });
            if (didMove)
                moved++;
            else if (movedToTrash)
                quarantined++;
            else
                left++;
            if (!completed)
                errors++;
        }
        return (moved, quarantined, left, errors);
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
        catch (UnauthorizedAccessException ex)
        {
            log.WriteLine($"Недостаточно прав для перемещения ярлыка «{path}». Ярлык оставлен на месте. " +
                "Если он находится на общем рабочем столе Windows (C:\\Users\\Public\\Desktop), " +
                "запустите UnsortedOrderer от имени администратора и повторите очистку. " +
                $"Подробности: {ex.Message}");
            return false;
        }
        catch (Exception ex) when (ex is IOException or COMException
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
