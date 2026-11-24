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

                var category = DesktopShortcutClassifier.GetFolder(info, Path.GetFileNameWithoutExtension(link));
                if (category is null)
                    return;

                var folder = Path.Combine(targetDesktop ?? desktop, category);
                if (ApplicationLocation.ContainsPath(folder, source) ||
                    destination is not null && ApplicationLocation.ContainsPath(folder, destination))
                    throw new IOException($"Группа совпадает с папкой сортировки: «{folder}».");
                if (File.Exists(folder) ||
                    Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Нет безопасной папки для группы «{folder}».");

                if (Directory.Exists(folder) && IsDuplicateInFolder(link, info, folder))
                {
                    if (destination is not null)
                    {
                        var quarantinedPath = new DeletionQuarantine(destination).MoveFile(link);
                        movedToTrash = true;
                        log.WriteLine($"Дублирующийся ярлык помещён в корзину: «{link}» → «{quarantinedPath}».");
                    }
                    return;
                }

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

    private bool IsDuplicateInFolder(string link, DesktopShortcut info, string folder)
    {
        foreach (var otherLink in Directory.EnumerateFiles(folder).Where(IsShortcut))
        {
            var otherInfo = store.Read(otherLink);
            if (otherInfo is not null && !store.IsBroken(otherLink, otherInfo) &&
                (SameShortcut(info, otherInfo) || SameEpsonApp(link, info, otherLink, otherInfo)))
                return true;
        }
        return false;
    }

    private static bool SameShortcut(DesktopShortcut first, DesktopShortcut second) =>
        !string.IsNullOrWhiteSpace(first.Target) &&
        string.Equals(first.Target.Trim().Trim('"'), second.Target.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(first.Arguments.Trim(), second.Arguments.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(first.WorkingDirectory.Trim(), second.WorkingDirectory.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameEpsonApp(string firstPath, DesktopShortcut first, string secondPath, DesktopShortcut second)
    {
        static bool IsEpson(string link, DesktopShortcut info)
        {
            var label = Path.GetFileNameWithoutExtension(link);
            var executable = Path.GetFileName(info.Target.Replace('\\', '/'));
            return label.StartsWith("Epson Photo+", StringComparison.OrdinalIgnoreCase) &&
                (executable.Equals("EPPlus.exe", StringComparison.OrdinalIgnoreCase) ||
                 executable.Equals("Epson Photo+.exe", StringComparison.OrdinalIgnoreCase) ||
                 executable.Equals("EpsonPhotoPlus.exe", StringComparison.OrdinalIgnoreCase));
        }

        if (!IsEpson(firstPath, first) || !IsEpson(secondPath, second)) return false;
        var firstDirectory = first.Target.Replace('\\', '/');
        var secondDirectory = second.Target.Replace('\\', '/');
        return string.Equals(firstDirectory[..firstDirectory.LastIndexOf('/')],
            secondDirectory[..secondDirectory.LastIndexOf('/')], StringComparison.OrdinalIgnoreCase);
    }

    public int DeduplicateGroup(string desktop, string category, string destination)
    {
        var folder = Path.Combine(desktop, category);
        if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            return 0;

        var seen = new List<(string Path, DesktopShortcut Info)>();
        var errors = 0;
        foreach (var link in Directory.EnumerateFiles(folder).Where(IsShortcut).ToArray())
        {
            if (!TryShortcutOperation(link, () =>
            {
                var info = store.Read(link);
                if (info is null) return;
                if (store.IsBroken(link, info))
                {
                    // Never prefer a stale launcher over a working one.
                    // Keep the normal broken-shortcut cleanup semantics.
                    var quarantine = new DeletionQuarantine(destination).MoveFile(link);
                    log.WriteLine($"Битый ярлык помещён в корзину: «{link}» → «{quarantine}».");
                }
                else if (seen.Any(previous => SameShortcut(info, previous.Info) ||
                    SameEpsonApp(link, info, previous.Path, previous.Info)))
                {
                    var quarantinedPath = new DeletionQuarantine(destination).MoveFile(link);
                    log.WriteLine($"Дублирующийся ярлык помещён в корзину: «{link}» → «{quarantinedPath}».");
                }
                else seen.Add((link, info));
            }))
                errors++;
        }
        return errors;
    }

    public void EnsureFolderLink(string desktop, string name, string target, string? commonDesktop = null, string? quarantineRoot = null)
    {
        if (!Directory.Exists(target))
            return;

        target = Path.GetFullPath(target);
        var matching = new List<string>();
        foreach (var root in new[] { desktop, commonDesktop }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in Directory.EnumerateFiles(root!).Where(IsWindowsShortcut))
            {
                TryShortcutOperation(file, () =>
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        return;
                    var info = store.Read(file);
                    if (info is not null && string.IsNullOrEmpty(info.Arguments) &&
                        Path.IsPathFullyQualified(info.Target) && ApplicationLocation.SamePath(info.Target, target))
                        matching.Add(file);
                });
            }
        }

        if (matching.Count > 0)
        {
            // Windows overlays personal and common desktop folders. Reuse the first
            // working shortcut and quarantine additional links to the exact same target.
            foreach (var duplicate in matching.Skip(1))
                if (quarantineRoot is not null)
                    TryShortcutOperation(duplicate, () =>
                    {
                        var saved = new DeletionQuarantine(quarantineRoot).MoveFile(duplicate);
                        log.WriteLine($"Дублирующийся ярлык {name} помещён в корзину: «{duplicate}» → «{saved}».");
                    });
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
