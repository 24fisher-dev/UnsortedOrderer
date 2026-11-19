using UnsortedOrderer.Contracts.Categories;
using UnsortedOrderer.Services;

namespace UnsortedOrderer.Categories;

public sealed class FirmwareCategory : FileCategory, INonSplittableDirectoryCategory
{
    private static readonly string[] FirmwareExtensions =
    [
        ".bin",
        ".dat"
    ];

    public FirmwareCategory(string folderName)
        : base("Firmware", folderName, FirmwareExtensions)
    {
    }

    internal static bool HasFirmwareName(string path) =>
        Path.GetFileName(path).Contains("firmware", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(path).Contains("прошивк", StringComparison.OrdinalIgnoreCase);

    internal static bool IsExplicitBundle(string path) =>
        Directory.Exists(path) && (HasFirmwareName(path) ||
            Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).Any(file =>
                FirmwareExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) && HasFirmwareName(file)));

    public bool IsNonSplittableDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        return Directory
            .EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Any(file => Matches(Path.GetExtension(file)));
    }

    public string GetDirectoryDestination(string destinationRoot, string directoryPath)
    {
        var categoryRoot = Path.Combine(destinationRoot, FolderName);
        Directory.CreateDirectory(categoryRoot);

        var directoryName = Path.GetFileName(directoryPath) ?? "Firmware";
        return FileUtilities.GetUniqueDirectoryPath(categoryRoot, directoryName);
    }

    public string GetFileDestination(string destinationRoot, string filePath)
    {
        return Path.Combine(destinationRoot, FolderName);
    }
}
