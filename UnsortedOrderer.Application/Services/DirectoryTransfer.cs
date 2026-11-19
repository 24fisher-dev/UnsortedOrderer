using Microsoft.VisualBasic.FileIO;
using UnsortedOrderer.Models;

namespace UnsortedOrderer.Services;

public static class DirectoryTransfer
{
    public static void Move(string source, string destination)
    {
        if (ApplicationLocation.ContainsPath(source, destination))
            throw new IOException("Нельзя перенести папку в неё саму или в её подпапку.");

        CheckDestinationDoesNotExist(destination);

        var sourceRoot = Path.GetPathRoot(Path.GetFullPath(source))!;
        var destinationRoot = Path.GetPathRoot(Path.GetFullPath(destination))!;
        if (ApplicationLocation.SamePath(sourceRoot, destinationRoot))
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) == (OperatingSystem.IsWindows() ? 17 : 18))
            {
                // Mounted volumes can also have the same textual path root.
            }
        }

        MoveAcrossVolumes(source, destination);
    }

    private static void MoveAcrossVolumes(string source, string destination)
    {
        // The framework's cross-volume move is file by file, without replacing existing files.
        // Reject links first so a directory junction is not followed into another tree.
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(source));
        while (pending.TryPop(out var folder))
        {
            CheckOrdinaryEntry(folder);
            foreach (var entry in folder.EnumerateFileSystemInfos())
            {
                CheckOrdinaryEntry(entry);
                if (entry is DirectoryInfo child)
                    pending.Push(child);
            }
        }

        CheckDestinationDoesNotExist(destination);
        FileSystem.MoveDirectory(source, destination, overwrite: false);
    }

    private static void CheckDestinationDoesNotExist(string destination)
    {
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("Папка назначения уже существует.");
    }

    private static void CheckOrdinaryEntry(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Перенос между томами не следует по ссылкам: {entry.FullName}");
    }
}
