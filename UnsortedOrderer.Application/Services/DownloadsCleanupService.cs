using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;

namespace UnsortedOrderer.Services;

public sealed class DownloadsCleanupService : IDownloadsCleanupService
{
    private readonly IMessageWriter _messageWriter;

    public DownloadsCleanupService(IMessageWriter messageWriter)
    {
        _messageWriter = messageWriter;
    }

    private static string? GetDownloadsPath()
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfilePath) ? null : Path.Combine(userProfilePath, "Downloads");
    }

    public bool CanClean(string destinationPath)
    {
        var downloadsPath = GetDownloadsPath();
        return !string.IsNullOrWhiteSpace(destinationPath)
            && downloadsPath is not null
            && Directory.Exists(downloadsPath)
            && !string.Equals(NormalizePath(downloadsPath), NormalizePath(destinationPath), StringComparison.OrdinalIgnoreCase);
    }

    public void Clean(string destinationPath)
    {
        if (!CanClean(destinationPath))
        {
            return;
        }

        var downloadsPath = GetDownloadsPath()!;
        var normalizedDestinationPath = NormalizePath(destinationPath);
        Directory.CreateDirectory(destinationPath);

        var skipped = 0;
        foreach (var entry in new DirectoryInfo(downloadsPath).EnumerateFileSystemInfos().ToArray())
        {
            var normalizedEntryPath = NormalizePath(entry.FullName);

            if (string.Equals(normalizedEntryPath, normalizedDestinationPath, StringComparison.OrdinalIgnoreCase) ||
                ApplicationLocation.Current.ProtectsFile(entry.FullName) ||
                entry is DirectoryInfo && (ApplicationLocation.Current.ProtectsDirectory(entry.FullName) ||
                    ApplicationLocation.ContainsPath(entry.FullName, destinationPath)))
            {
                continue;
            }

            var destination = EnsureUniquePath(Path.Combine(destinationPath, entry.Name));
            try
            {
                MoveEntry(entry, destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                _messageWriter.WriteLine($"Не удалось полностью перенести «{entry.FullName}» → «{destination}»: {ex.Message}");
            }
        }

        _messageWriter.WriteLine(skipped == 0 ? "Загрузки обработаны." : $"Загрузки обработаны. Ошибок переноса: {skipped}.");
        _messageWriter.WriteLine(string.Empty);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

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
