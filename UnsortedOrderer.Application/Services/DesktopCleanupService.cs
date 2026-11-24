using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;
using UnsortedOrderer.Services.Desktop;

namespace UnsortedOrderer.Services;

public sealed class DesktopCleanupService : IDesktopCleanupService
{
    private readonly IMessageWriter _messageWriter;

    private readonly string _desktopPath;
    private readonly string _commonDesktopPath;
    private readonly ApplicationLocation _application;
    private readonly DesktopShortcutOrganizer _shortcuts;
    private readonly Action<FileSystemInfo, string> _moveEntry;

    public DesktopCleanupService(IMessageWriter messageWriter)
        : this(messageWriter, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ApplicationLocation.Current,
            commonDesktopPath: Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory))
    {
    }

    public DesktopCleanupService(IMessageWriter messageWriter, string desktopPath, ApplicationLocation application,
        IDesktopShortcutStore? shortcutStore = null, Action<FileSystemInfo, string>? moveEntry = null,
        string? commonDesktopPath = null)
    {
        _messageWriter = messageWriter;
        _desktopPath = desktopPath;
        _commonDesktopPath = commonDesktopPath ?? string.Empty;
        _application = application;
        _shortcuts = new DesktopShortcutOrganizer(shortcutStore ?? new DesktopShortcutStore(), messageWriter);
        _moveEntry = moveEntry ?? MoveEntry;
    }

    public string DesktopPath => _desktopPath;
    public string CommonDesktopPath => _commonDesktopPath;

    public bool CanClean(string destinationPath) => CanProcessRoot(_desktopPath, destinationPath);

    public bool CanCleanCommon(string destinationPath) => CanProcessRoot(_commonDesktopPath, destinationPath)
        && (string.IsNullOrWhiteSpace(_desktopPath) ||
            !ApplicationLocation.SamePath(_desktopPath, _commonDesktopPath));

    private static bool CanProcessRoot(string desktopPath, string unsortedPath) =>
        !string.IsNullOrWhiteSpace(unsortedPath)
        && Directory.Exists(desktopPath)
        && !ApplicationLocation.ContainsPath(unsortedPath, desktopPath);

    public int Clean(string destinationPath, string? sortedPath = null) =>
        CleanSelected(destinationPath, sortedPath, personal: true, common: false);

    public int CleanCommon(string destinationPath, string? sortedPath = null) =>
        CleanSelected(destinationPath, sortedPath, personal: false, common: true);

    public int CleanSelected(string unsortedPath, string? sortedPath, bool personal, bool common)
    {
        var roots = new List<DesktopRoot>();
        var errors = 0;
        if (personal)
            Prepare(_desktopPath, "Личный рабочий стол", CanClean(unsortedPath));
        if (common)
            Prepare(_commonDesktopPath, "Общий рабочий стол", CanCleanCommon(unsortedPath));

        // Explorer overlays the personal and shared desktops. Keep a single visible folder
        // per category, then route both roots' shortcuts into that same folder.
        // Only selected roots are consolidated; unchecked roots remain untouched.
        var categoryDesktop = personal && roots.Any(root => ApplicationLocation.SamePath(root.Path, _desktopPath))
            ? _desktopPath : roots.FirstOrDefault()?.Path;
        if (categoryDesktop is not null)
        {
            errors += DesktopShortcutGroupFolder.Consolidate(
                roots.Select(root => root.Path), categoryDesktop, unsortedPath, sortedPath, _messageWriter);
        }

        // Resolve shortcuts on both roots before moving any ordinary desktop entry.
        // A shared shortcut can point to an entry on the personal desktop or vice versa.
        var shortcutResults = roots.Select(root => _shortcuts.Group(root.Path, unsortedPath, sortedPath,
            root.Entries.Where(IsShortcut).Select(entry => entry.FullName), categoryDesktop)).ToArray();

        for (var i = 0; i < roots.Count; i++)
        {
            var root = roots[i];
            var shortcuts = shortcutResults[i];
            var (moved, failed) = MoveEntries(root.Entries, unsortedPath, sortedPath);
            failed += shortcuts.Errors;
            errors += failed;

            if (moved == 0 && shortcuts.Moved == 0 && shortcuts.Quarantined == 0)
                _messageWriter.WriteLine($"{root.Label}: ничего не перемещено.");
            _messageWriter.WriteLine($"{root.Label}: объектов в папку разбора — {moved}. Ярлыков сгруппировано — {shortcuts.Moved}. " +
                $"Битых ярлыков в корзине — {shortcuts.Quarantined}. Ярлыков оставлено — {shortcuts.Left}. Ошибок переноса: {failed}.");
            _messageWriter.WriteLine(string.Empty);
        }
        return errors;

        void Prepare(string path, string label, bool available)
        {
            if (!available || (sortedPath is not null && ApplicationLocation.ContainsPath(sortedPath, path)))
            {
                _messageWriter.WriteLine($"{label} не обработан: путь недоступен или пересекается с папками сортировки («{path}»).");
                errors++;
                return;
            }

            _messageWriter.WriteLine($"{label}: «{path}».");
            try
            {
                var entries = new DirectoryInfo(path).GetFileSystemInfos();
                Directory.CreateDirectory(unsortedPath);
                roots.Add(new DesktopRoot(path, label, entries));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _messageWriter.WriteLine($"Не удалось прочитать или подготовить папки «{path}» → «{unsortedPath}»: {ex.Message}");
                errors++;
            }
        }
    }

    private (int Moved, int Failed) MoveEntries(FileSystemInfo[] entries, string unsortedPath, string? sortedPath)
    {
        var moved = 0;
        var failed = 0;
        foreach (var entry in entries)
        {
            if (IsShortcut(entry) || entry.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
                || entry is FileInfo && entry.Extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                || _application.ProtectsFile(entry.FullName) ||
                (entry is DirectoryInfo && (_application.ProtectsDirectory(entry.FullName)
                    || DesktopShortcutOrganizer.IsGroupFolder(entry.Name)
                    || ApplicationLocation.ContainsPath(entry.FullName, unsortedPath)
                    || sortedPath is not null && ApplicationLocation.ContainsPath(entry.FullName, sortedPath))))
                continue;

            var destination = EnsureUniquePath(Path.Combine(unsortedPath, entry.Name));
            try
            {
                if (entry.LinkTarget is not null)
                {
                    _messageWriter.WriteLine($"Ссылка файловой системы оставлена на месте: «{entry.FullName}».");
                    continue;
                }

                _moveEntry(entry, destination);
                moved++;
                _messageWriter.WriteLine($"Перенесено в папку разбора: «{entry.FullName}» → «{destination}».");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                _messageWriter.WriteLine($"Не удалось полностью перенести «{entry.FullName}» → «{destination}»: {ex.Message}");
            }
        }

        return (moved, failed);
    }

    private sealed record DesktopRoot(string Path, string Label, FileSystemInfo[] Entries);

    public void CreateFolderShortcuts(string unsortedPath, string sortedPath)
    {
        if (!CanClean(unsortedPath) || ApplicationLocation.ContainsPath(sortedPath, _desktopPath))
            return;

        _shortcuts.EnsureFolderLink(_desktopPath, "Unsorted", unsortedPath);
        _shortcuts.EnsureFolderLink(_desktopPath, "Sorted", sortedPath);
    }

    private static bool IsShortcut(FileSystemInfo entry) =>
        entry is FileInfo && DesktopShortcutOrganizer.IsShortcut(entry.FullName);

    private static string EnsureUniquePath(string destinationPath)
    {
        if (!File.Exists(destinationPath) && !Directory.Exists(destinationPath))
        {
            return destinationPath;
        }

        var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        var counter = 1;

        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{baseName} ({counter}){extension}");
            counter++;
        }
        while (File.Exists(candidate) || Directory.Exists(candidate));

        return candidate;
    }

    private static void MoveEntry(FileSystemInfo entry, string destinationPath)
    {
        if (entry is DirectoryInfo)
        {
            DirectoryTransfer.Move(entry.FullName, destinationPath);
            return;
        }

        File.Move(entry.FullName, destinationPath);
    }
}
