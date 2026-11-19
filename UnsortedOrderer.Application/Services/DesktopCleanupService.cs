using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;
using UnsortedOrderer.Services.Desktop;

namespace UnsortedOrderer.Services;

public sealed class DesktopCleanupService : IDesktopCleanupService
{
    private readonly IMessageWriter _messageWriter;

    private readonly string _desktopPath;
    private readonly ApplicationLocation _application;
    private readonly DesktopShortcutOrganizer _shortcuts;
    private readonly Action<FileSystemInfo, string> _moveEntry;

    public DesktopCleanupService(IMessageWriter messageWriter)
        : this(messageWriter, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ApplicationLocation.Current)
    {
    }

    public DesktopCleanupService(IMessageWriter messageWriter, string desktopPath, ApplicationLocation application,
        IDesktopShortcutStore? shortcutStore = null, Action<FileSystemInfo, string>? moveEntry = null)
    {
        _messageWriter = messageWriter;
        _desktopPath = desktopPath;
        _application = application;
        _shortcuts = new DesktopShortcutOrganizer(shortcutStore ?? new DesktopShortcutStore(), messageWriter);
        _moveEntry = moveEntry ?? MoveEntry;
    }

    public bool CanClean(string destinationPath)
    {
        var desktopPath = _desktopPath;
        return !string.IsNullOrWhiteSpace(destinationPath)
            && Directory.Exists(desktopPath)
            && !ApplicationLocation.SamePath(desktopPath, destinationPath)
            && _application.IsInside(desktopPath);
    }

    public void Clean(string destinationPath, string? sortedPath = null)
    {
        if (!CanClean(destinationPath))
        {
            return;
        }

        var desktopPath = _desktopPath;
        Directory.CreateDirectory(destinationPath);

        var skipped = 0;
        foreach (var entry in new DirectoryInfo(desktopPath).EnumerateFileSystemInfos().ToArray())
        {
            if (IsShortcut(entry) || _application.ProtectsFile(entry.FullName) ||
                (entry is DirectoryInfo && (_application.ProtectsDirectory(entry.FullName)
                    || DesktopShortcutOrganizer.IsGroupFolder(entry.Name)
                    || ApplicationLocation.ContainsPath(entry.FullName, destinationPath)
                    || sortedPath is not null && ApplicationLocation.ContainsPath(entry.FullName, sortedPath))))
            {
                continue;
            }

            var destination = EnsureUniquePath(Path.Combine(destinationPath, entry.Name));
            try
            {
                _moveEntry(entry, destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                _messageWriter.WriteLine($"Не удалось полностью перенести «{entry.FullName}» → «{destination}»: {ex.Message}");
            }
        }

        _shortcuts.Group(desktopPath, destinationPath, sortedPath);
        _messageWriter.WriteLine(skipped == 0 ? "Рабочий стол обработан." : $"Рабочий стол обработан. Ошибок переноса: {skipped}.");
        _messageWriter.WriteLine(string.Empty);
    }

    public void CreateFolderShortcuts(string unsortedPath, string sortedPath)
    {
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
