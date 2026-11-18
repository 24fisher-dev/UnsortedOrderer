namespace UnsortedOrderer.Contracts.Services;

public interface IDesktopCleanupService
{
    bool CanClean(string destinationPath);
    void Clean(string destinationPath);
}
