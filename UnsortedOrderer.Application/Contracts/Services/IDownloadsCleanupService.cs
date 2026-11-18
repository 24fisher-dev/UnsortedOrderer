namespace UnsortedOrderer.Contracts.Services;

public interface IDownloadsCleanupService
{
    bool CanClean(string destinationPath);
    void Clean(string destinationPath);
}
