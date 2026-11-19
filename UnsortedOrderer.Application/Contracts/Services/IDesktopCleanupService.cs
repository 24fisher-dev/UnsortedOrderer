namespace UnsortedOrderer.Contracts.Services;

public interface IDesktopCleanupService
{
    bool CanClean(string destinationPath);
    void Clean(string destinationPath, string? sortedPath = null);
    void CreateFolderShortcuts(string unsortedPath, string sortedPath);
}
