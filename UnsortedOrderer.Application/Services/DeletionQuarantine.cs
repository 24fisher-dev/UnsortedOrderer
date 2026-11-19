using UnsortedOrderer.Models;

namespace UnsortedOrderer.Services;

internal sealed class DeletionQuarantine(string destinationRoot)
{
    public const string FolderName = "_ToDelete";
    public string DirectoryPath { get; } = Path.Combine(Path.GetFullPath(destinationRoot), FolderName);

    public string MoveFile(string path)
    {
        EnsureOrdinaryPath();
        return FileUtilities.MoveFile(path, DirectoryPath);
    }

    public string MoveDirectory(string path)
    {
        EnsureOrdinaryPath();
        Directory.CreateDirectory(DirectoryPath);
        var target = FileUtilities.GetUniqueDirectoryPath(DirectoryPath, Path.GetFileName(path));
        DirectoryTransfer.Move(path, target);
        return target;
    }

    public void Clear(ApplicationLocation application, string sourceDirectory)
    {
        EnsureOrdinaryPath();
        if (!Directory.Exists(DirectoryPath))
            return;

        if (application.ProtectsDirectory(DirectoryPath) ||
            ApplicationLocation.ContainsPath(DirectoryPath, sourceDirectory))
            throw new IOException($"Корзина содержит программу или папку разбора: {DirectoryPath}");

        // Inspect the complete tree first. A junction or symlink must never expand the scope
        // of deletion, even if someone added it to the trash outside the application.
        var directories = new List<DirectoryInfo> { new(DirectoryPath) };
        var files = new List<FileSystemInfo>();
        for (var i = 0; i < directories.Count; i++)
        {
            foreach (var entry in directories[i].EnumerateFileSystemInfos())
            {
                RejectLink(entry.Attributes, entry.FullName);
                if (entry is DirectoryInfo folder)
                    directories.Add(folder);
                else
                    files.Add(entry);
            }
        }

        foreach (var file in files)
        {
            EnsureOrdinaryPath(file.FullName);
            File.Delete(file.FullName);
        }

        for (var i = directories.Count - 1; i > 0; i--)
        {
            EnsureOrdinaryPath(directories[i].FullName);
            Directory.Delete(directories[i].FullName);
        }
    }

    private void EnsureOrdinaryPath(string? entry = null)
    {
        for (var path = entry ?? DirectoryPath; path is not null; path = Path.GetDirectoryName(path))
        {
            try
            {
                var attributes = File.GetAttributes(path);
                RejectLink(attributes, path);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void RejectLink(FileAttributes attributes, string path)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Операция с корзиной не следует по ссылкам: {path}");
    }
}
