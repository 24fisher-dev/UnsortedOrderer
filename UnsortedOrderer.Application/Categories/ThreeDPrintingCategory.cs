using System.Globalization;
using UnsortedOrderer.Application.Services.Categories;
using UnsortedOrderer.Contracts.Categories;
using UnsortedOrderer.Services;

namespace UnsortedOrderer.Categories;

public sealed class ThreeDPrintingCategory : FileCategory, INonSplittableDirectoryCategory
{
    private static readonly HashSet<string> ProjectSidecars = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".txt", ".md", ".pdf", ".json",
        ".xml", ".ini", ".cfg", ".conf", ".zip", ".7z", ".rar"
    };

    public ThreeDPrintingCategory()
        : base("3DPrinting", "3DPrinting", [
            ".stl", ".3mf", ".gcode", ".bgcode", ".photon", ".pwmo", ".curaprofile",
            ".obj", ".f3d", ".skp", ".dxf", ".dwg", ".3dm", ".sldprt", ".sldasm", ".ipt", ".iam", ".fcstd",
            ".fbx", ".3ds", ".dae", ".ply", ".blend", ".gltf", ".glb", ".usdz", ".step", ".stp", ".iges", ".igs", ".scad"
        ])
    {
    }

    public bool IsMaterialProfile(string path) => FilamentProfileDetector.IsMatch(path);

    public bool IsNonSplittableDirectory(string path)
    {
        // Only classify a project folder when it contains a print model itself.
        // Otherwise a parent containing multiple unrelated project folders could be swallowed.
        var root = new DirectoryInfo(path);
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
            return false;

        var files = root.GetFiles();
        var models = files.Where(file => Matches(file.Extension)).ToArray();
        if (models.Length == 0)
            return false;

        var allFiles = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            foreach (var entry in folder.EnumerateFileSystemInfos())
            {
                // Never follow a link or absorb an installer/repository inside a print project.
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;

                if (entry is DirectoryInfo child)
                {
                    if (child.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                        return false;
                    pending.Push(child);
                    continue;
                }

                var extension = Path.GetExtension(entry.Name);
                if (!Matches(extension) && !ProjectSidecars.Contains(extension))
                    return false;
                allFiles++;
            }
        }

        // One isolated model should keep the existing file-by-file date layout.
        return allFiles >= 2;
    }

    public string GetDirectoryDestination(string destinationRoot, string directoryPath)
    {
        var model = new DirectoryInfo(directoryPath).GetFiles()
            .First(file => Matches(file.Extension));
        var folder = GetDestinationDirectory(destinationRoot, model.FullName);
        Directory.CreateDirectory(folder);
        return FileUtilities.GetUniqueDirectoryPath(folder, Path.GetFileName(directoryPath));
    }

    public string GetFileDestination(string destinationRoot, string filePath) =>
        GetDestinationDirectory(destinationRoot, filePath);

    public string GetDestinationDirectory(string destinationRoot, string filePath)
    {
        var date = File.GetCreationTime(filePath);
        if (date.Year < 1980)
        {
            date = File.GetLastWriteTime(filePath);
        }

        return Path.Combine(destinationRoot, FolderName, date.ToString("yyyy", CultureInfo.InvariantCulture),
            date.ToString("MM", CultureInfo.InvariantCulture));
    }
}
