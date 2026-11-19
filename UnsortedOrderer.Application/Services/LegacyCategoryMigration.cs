using UnsortedOrderer.Application.Services.Categories;
using UnsortedOrderer.Categories;
using UnsortedOrderer.Contracts.Categories;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;

namespace UnsortedOrderer.Services;

internal sealed class LegacyCategoryMigration(
    AppSettings settings, ApplicationLocation application, IEnumerable<ICategory> categories, IMessageWriter log)
{
    public void Run()
    {
        var root = settings.DestinationRoot;
        if (ApplicationLocation.ContainsPath(settings.SourceDirectory, root) ||
            ApplicationLocation.ContainsPath(root, settings.SourceDirectory) ||
            !IsPlainDirectory(root))
            return;

        if (categories.OfType<ThreeDPrintingCategory>().FirstOrDefault() is { } printing)
            Consolidate("3DModels", printing.FolderName, file =>
                printing.Matches(file.Extension) ? printing.GetDestinationDirectory(root, file.FullName) : null);

        if (settings.MusicalInstrumentsFolderName == "Guitar Tabs")
            Consolidate("Instruments", "Guitar Tabs");

        var drivers = categories.OfType<DriversCategory>().FirstOrDefault();
        var soft = categories.OfType<SoftCategory>().FirstOrDefault();
        if (drivers is not null && soft is not null)
        {
            RepairPackages(settings.FirmwareFolderName, path =>
                FirmwareCategory.IsExplicitBundle(path) || RepositoriesService.HasVersionControlMarker(path) ? null :
                drivers.IsNonSplittableDirectory(path) ? drivers :
                soft.IsNonSplittableDirectory(path) ? soft : null);
            RepairPackages(settings.RepositoriesFolderName, path =>
                !RepositoriesService.HasVersionControlMarker(path) && SoftwareDistributivesService.IsKnownProductBundle(path)
                    ? soft : null);
        }
    }

    private void Consolidate(string oldName, string newName, Func<FileInfo, string?>? destinationForFile = null)
    {
        var source = Path.Combine(settings.DestinationRoot, oldName);
        var target = Path.Combine(settings.DestinationRoot, newName);
        if (!CanMoveDirectory(source))
            return;

        TryMove(source, () =>
        {
            EnsurePlainTarget(target);
            foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos().ToArray())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || application.ProtectsFile(entry.FullName))
                    continue;
                TryMove(entry.FullName, () =>
                {
                    if (entry is DirectoryInfo directory && CanMoveDirectory(directory.FullName))
                        DirectoryTransfer.Move(directory.FullName, FileUtilities.GetUniqueDirectoryPath(target, directory.Name));
                    else if (entry is FileInfo file)
                    {
                        var folder = destinationForFile?.Invoke(file) ?? target;
                        EnsurePlainTarget(folder);
                        FileUtilities.MoveFile(file.FullName, folder);
                    }
                });
            }

            if (!Directory.EnumerateFileSystemEntries(source).Any())
                Directory.Delete(source);
        });
    }

    private void RepairPackages(string oldName, Func<string, INonSplittableDirectoryCategory?> classify)
    {
        var source = Path.Combine(settings.DestinationRoot, oldName);
        if (!IsPlainDirectory(source))
            return;

        TryMove(source, () =>
        {
            foreach (var directory in Directory.GetDirectories(source).Where(CanMoveDirectory))
            {
                TryMove(directory, () =>
                {
                    var category = classify(directory);
                    if (category is null || ApplicationLocation.SamePath(source, Path.Combine(settings.DestinationRoot, category.FolderName)))
                        return;

                    EnsurePlainTarget(Path.Combine(settings.DestinationRoot, category.FolderName));
                    var destination = category.GetDirectoryDestination(settings.DestinationRoot, directory);
                    DirectoryTransfer.Move(directory, destination);
                    log.WriteLine($"Перенесена ошибочно отсортированная папка «{directory}» → «{destination}».");
                });
            }
        });
    }

    private bool CanMoveDirectory(string path) =>
        IsPlainDirectory(path) && !application.ProtectsDirectory(path);

    private void TryMove(string path, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.WriteLine($"Не удалось обновить старую сортировку для «{path}»: {ex.Message}");
        }
    }

    private static bool IsPlainDirectory(string path) =>
        Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private void EnsurePlainTarget(string path)
    {
        for (var current = Path.GetFullPath(path); !ApplicationLocation.SamePath(current, settings.DestinationRoot);
             current = Path.GetDirectoryName(current)!)
        {
            if (Directory.Exists(current) && !IsPlainDirectory(current))
                throw new IOException($"Папка назначения является ссылкой: {current}");
            if (!ApplicationLocation.ContainsPath(settings.DestinationRoot, current))
                throw new IOException($"Папка назначения находится за пределами Sorted: {current}");
        }

        Directory.CreateDirectory(path);
    }
}
