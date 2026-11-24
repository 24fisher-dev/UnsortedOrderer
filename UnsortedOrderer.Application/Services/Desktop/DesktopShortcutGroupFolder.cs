using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;

namespace UnsortedOrderer.Services.Desktop;

/// <summary>
/// Windows Explorer overlays the user and public desktop, so a category folder in each
/// location appears twice on screen. Merge selected desktop category folders into one.
/// </summary>
public static class DesktopShortcutGroupFolder
{
    private static readonly string[] Categories =
    [
        DesktopShortcutClassifier.GamesFolder, DesktopShortcutClassifier.EditorsFolder,
        DesktopShortcutClassifier.UtilitiesFolder, DesktopShortcutClassifier.MusicFolder,
        DesktopShortcutClassifier.VrFolder, DesktopShortcutClassifier.DevelopmentFolder,
        DesktopShortcutClassifier.BrowsersFolder, DesktopShortcutClassifier.CommunicationFolder,
        DesktopShortcutClassifier.PrintingFolder
    ];

    public static string? ParseCategory(string name)
    {
        foreach (var category in Categories)
        {
            if (name.Equals(category, StringComparison.OrdinalIgnoreCase))
                return category;

            if (!name.StartsWith(category, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = name[category.Length..].TrimStart();
            if (remainder.Length < 3 || remainder[0] != '(' || remainder[^1] != ')' ||
                !int.TryParse(remainder[1..^1], out var suffix) || suffix < 1)
                continue;

            return category;
        }
        return null;
    }

    public static int Consolidate(IEnumerable<string> selectedDesktops, string preferredDesktop,
        string unsortedPath, string? sortedPath, ApplicationLocation application,
        IReadOnlyCollection<string> shortcutTargets, IMessageWriter log)
    {
        var errors = 0;
        foreach (var desktop in selectedDesktops.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] folders;
            try
            {
                folders = Directory.GetDirectories(desktop);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.WriteLine($"Не удалось проверить папки категорий «{desktop}»: {ex.Message}");
                errors++;
                continue;
            }

            foreach (var sourceFolder in folders)
            {
                var category = ParseCategory(Path.GetFileName(sourceFolder));
                if (category is null)
                    continue;

                var targetFolder = Path.Combine(preferredDesktop, category);
                if (ApplicationLocation.SamePath(sourceFolder, targetFolder) ||
                    application.ProtectsDirectory(sourceFolder) ||
                    shortcutTargets.Any(target => ApplicationLocation.ContainsPath(sourceFolder, target)))
                    continue;

                // Never walk into junctions, symlinks or the configured sorting roots.
                if (ApplicationLocation.ContainsPath(sourceFolder, unsortedPath) ||
                    ApplicationLocation.ContainsPath(targetFolder, unsortedPath) ||
                    sortedPath is not null && (ApplicationLocation.ContainsPath(sourceFolder, sortedPath) ||
                        ApplicationLocation.ContainsPath(targetFolder, sortedPath)))
                    continue;

                try
                {
                    if ((File.GetAttributes(sourceFolder) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Папка является ссылкой файловой системы.");
                    if (File.Exists(targetFolder) ||
                        Directory.Exists(targetFolder) &&
                        (File.GetAttributes(targetFolder) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Целевая папка занята файлом или ссылкой.");

                    // Create only when there are entries to transfer. Do not leave an empty
                    // target category after a no-op, or duplicate folders after repeated runs.
                    foreach (var entry in new DirectoryInfo(sourceFolder).EnumerateFileSystemInfos())
                    {
                        if (application.ProtectsFile(entry.FullName) ||
                            entry is DirectoryInfo && application.ProtectsDirectory(entry.FullName))
                            throw new IOException($"Нельзя переместить работающую программу или её настройки: {entry.FullName}");
                        Directory.CreateDirectory(targetFolder);
                        var target = Path.Combine(targetFolder, entry.Name);
                        if (entry is DirectoryInfo)
                        {
                            target = FileUtilities.GetUniqueDirectoryPath(targetFolder, entry.Name);
                            DirectoryTransfer.Move(entry.FullName, target);
                        }
                        else
                        {
                            target = FileUtilities.GetUniqueFilePath(target);
                            File.Move(entry.FullName, target);
                        }

                        log.WriteLine($"Объединена категория {category}: «{entry.FullName}» → «{target}».");
                    }

                    Directory.Delete(sourceFolder);
                    log.WriteLine($"Убрана дублирующаяся папка категории: «{sourceFolder}».");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors++;
                    log.WriteLine($"Не удалось объединить категорию «{sourceFolder}» → «{targetFolder}»: {ex.Message}");
                    // Keep source and all untransferred entries untouched on failure.
                }
            }
        }
        return errors;
    }
}
