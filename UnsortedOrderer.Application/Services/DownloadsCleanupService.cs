using UnsortedOrderer.Contracts.Services;

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

        foreach (var entry in new DirectoryInfo(downloadsPath).EnumerateFileSystemInfos())
        {
            var normalizedEntryPath = NormalizePath(entry.FullName);

            if (string.Equals(normalizedEntryPath, normalizedDestinationPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = EnsureUniquePath(Path.Combine(destinationPath, entry.Name));
            MoveEntry(entry, destination);
        }

        _messageWriter.WriteLine("Downloads folder cleaned.");
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
            Directory.Move(entry.FullName, destinationPath);
            return;
        }

        File.Move(entry.FullName, destinationPath);
    }
}
