using System.Globalization;
using UnsortedOrderer.Application.Services.Categories;

namespace UnsortedOrderer.Categories;

public sealed class ThreeDPrintingCategory : FileCategory
{
    public ThreeDPrintingCategory()
        : base("3DPrinting", "3DPrinting", [
            ".stl", ".3mf", ".gcode", ".bgcode", ".photon", ".pwmo", ".curaprofile",
            ".obj", ".f3d", ".skp", ".dxf", ".dwg", ".3dm", ".sldprt", ".sldasm", ".ipt", ".iam", ".fcstd",
            ".fbx", ".3ds", ".dae", ".ply", ".blend", ".gltf", ".glb", ".usdz", ".step", ".stp", ".iges", ".igs", ".scad"
        ])
    {
    }

    public bool IsMaterialProfile(string path) => FilamentProfileDetector.IsMatch(path);

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
