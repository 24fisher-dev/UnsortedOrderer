using System.Collections.Generic;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;

namespace UnsortedOrderer.Services;

public sealed class DesktopCleanupService : IDesktopCleanupService
{
    private static readonly HashSet<string> ShortcutExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk",
        ".url",
    };

    private readonly IMessageWriter _messageWriter;

    private readonly string _desktopPath;
    private readonly ApplicationLocation _application;

    public DesktopCleanupService(IMessageWriter messageWriter)
        : this(messageWriter, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ApplicationLocation.Current)
    {
    }

    public DesktopCleanupService(IMessageWriter messageWriter, string desktopPath, ApplicationLocation application)
    {
        _messageWriter = messageWriter;
        _desktopPath = desktopPath;
        _application = application;
    }

    public bool CanClean(string destinationPath)
    {
        var desktopPath = _desktopPath;
        return !string.IsNullOrWhiteSpace(destinationPath)
            && Directory.Exists(desktopPath)
            && !ApplicationLocation.SamePath(desktopPath, destinationPath)
            && _application.IsInside(desktopPath);
    }

    public void Clean(string destinationPath)
    {
        if (!CanClean(destinationPath))
        {
            return;
        }

        var desktopPath = _desktopPath;
        Directory.CreateDirectory(destinationPath);

        foreach (var entry in new DirectoryInfo(desktopPath).EnumerateFileSystemInfos())
        {
            if (IsShortcut(entry) || _application.ProtectsFile(entry.FullName) ||
                (entry is DirectoryInfo && (_application.ProtectsDirectory(entry.FullName)
                    || ApplicationLocation.ContainsPath(entry.FullName, destinationPath))))
            {
                continue;
            }

            var destination = EnsureUniquePath(Path.Combine(destinationPath, entry.Name));
            MoveEntry(entry, destination);
        }

        _messageWriter.WriteLine("Desktop cleaned.");
        _messageWriter.WriteLine(string.Empty);
    }

    private static bool IsShortcut(FileSystemInfo entry)
    {
        return entry is FileInfo fileInfo && ShortcutExtensions.Contains(fileInfo.Extension);
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
