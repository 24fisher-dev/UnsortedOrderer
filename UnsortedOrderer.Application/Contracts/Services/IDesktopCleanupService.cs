namespace UnsortedOrderer.Contracts.Services;

public interface IDesktopCleanupService
{
    bool CanClean(string destinationPath);
    int Clean(string destinationPath, string? sortedPath = null);
    int CleanCommon(string destinationPath, string? sortedPath = null);
    void CreateFolderShortcuts(string unsortedPath, string sortedPath);
}
