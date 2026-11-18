using System.Globalization;
using UnsortedOrderer.Application.Services.Categories;

namespace UnsortedOrderer.Categories;

public sealed class ThreeDPrintingCategory : FileCategory
{
    public ThreeDPrintingCategory()
        : base("3DPrinting", "3Дпечать", [".stl", ".3mf", ".gcode", ".bgcode"])
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
