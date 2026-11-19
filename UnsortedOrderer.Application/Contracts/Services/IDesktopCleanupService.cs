namespace UnsortedOrderer.Contracts.Services;

public interface IDesktopCleanupService
{
    bool CanClean(string destinationPath);
    int Clean(string destinationPath, string? sortedPath = null);
    int CleanCommon(string destinationPath, string? sortedPath = null);
    int CleanSelected(string destinationPath, string? sortedPath, bool personal, bool common);
    void CreateFolderShortcuts(string unsortedPath, string sortedPath);
}
