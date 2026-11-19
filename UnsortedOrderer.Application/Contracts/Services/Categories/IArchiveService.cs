namespace UnsortedOrderer.Application.Contracts.Services.Categories;

public interface IArchiveService
{
    ArchiveHandlingResult HandleArchiveFile(string archivePath, string categoryName);

    void RecordCompanionFileMove(string sourcePath, string destinationDirectory, string categoryName);

    string HandleArchive(string archivePath, string destinationDirectory);
}

public sealed record ArchiveHandlingResult(string DestinationPath, string CategoryName);
