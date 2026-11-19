using UnsortedOrderer.Application.Services.Categories;
using UnsortedOrderer.Contracts.Categories;
using UnsortedOrderer.Services;

namespace UnsortedOrderer.Categories;

public sealed class DataCategory() : FileCategory("Data", "Data", [".xls", ".xlsx", ".xlsm", ".xlsb", ".csv", ".ods", ".tsv", ".parquet", ".feather", ".avro"]),
    INonSplittableDirectoryCategory
{
    public bool IsNonSplittableDirectory(string path)
    {
        var foundXml = false;
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if (entry is not FileInfo || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            if (entry.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) && FileFormatProbe.IsDataXml(entry.FullName))
            {
                foundXml = true;
            }
            else if (!Matches(entry.Extension) && !IsFolderMetadata(entry.Name))
            {
                return false;
            }
        }

        return foundXml;
    }

    public string GetDirectoryDestination(string destinationRoot, string directoryPath)
    {
        var root = Path.Combine(destinationRoot, FolderName);
        Directory.CreateDirectory(root);
        return FileUtilities.GetUniqueDirectoryPath(root, Path.GetFileName(directoryPath));
    }

    public string GetFileDestination(string destinationRoot, string filePath) =>
        Path.Combine(destinationRoot, FolderName);

    private static bool IsFolderMetadata(string name) => name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
        || name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase);
}
