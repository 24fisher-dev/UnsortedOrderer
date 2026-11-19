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
        CleanRoot(_desktopPath, destinationPath, sortedPath, shared: false);

    public int CleanCommon(string destinationPath, string? sortedPath = null) =>
        CleanRoot(_commonDesktopPath, destinationPath, sortedPath, shared: true);

    private int CleanRoot(string desktopPath, string unsortedPath, string? sortedPath, bool shared)
    {
        var label = shared ? "Общий рабочий стол" : "Личный рабочий стол";
        if (!(shared ? CanCleanCommon(unsortedPath) : CanClean(unsortedPath)) ||
            (sortedPath is not null && ApplicationLocation.ContainsPath(sortedPath, desktopPath)))
        {
            _messageWriter.WriteLine($"{label} не обработан: путь недоступен или пересекается с папками сортировки («{desktopPath}»).");
            return 1;
        }

        _messageWriter.WriteLine($"{label}: «{desktopPath}».");
        FileSystemInfo[] entries;
        try
        {
            entries = new DirectoryInfo(desktopPath).GetFileSystemInfos();
            if (!shared)
                Directory.CreateDirectory(unsortedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _messageWriter.WriteLine($"Не удалось прочитать или подготовить папки «{desktopPath}» → «{unsortedPath}»: {ex.Message}");
            return 1;
        }

        var moved = 0;
        // A shortcut may point to a desktop file or directory this run is about to move.
        // Judge its existing target before the import changes the original path.
        var shortcuts = _shortcuts.Group(desktopPath, unsortedPath, sortedPath, entries.Where(IsShortcut).Select(entry => entry.FullName));
        var failed = shortcuts.Errors;
        if (!shared)
        {
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
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed++;
                    _messageWriter.WriteLine($"Не удалось полностью перенести «{entry.FullName}» → «{destination}»: {ex.Message}");
                }
            }
        }

        if (moved == 0 && shortcuts.Moved == 0 && shortcuts.Quarantined == 0)
            _messageWriter.WriteLine($"{label}: ничего не перемещено.");
        _messageWriter.WriteLine($"{label}: объектов в папку разбора — {moved}. Ярлыков сгруппировано — {shortcuts.Moved}. " +
            $"Битых ярлыков в корзине — {shortcuts.Quarantined}. Ярлыков оставлено — {shortcuts.Left}. Ошибок переноса: {failed}.");
        _messageWriter.WriteLine(string.Empty);
        return failed;
    }

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
