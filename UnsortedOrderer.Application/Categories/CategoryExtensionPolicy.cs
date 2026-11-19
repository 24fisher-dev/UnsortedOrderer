using UnsortedOrderer.Contracts.Categories;

namespace UnsortedOrderer.Categories;

public static class CategoryExtensionPolicy
{
    // Image selection uses the photo probe. GPS/Guitar Pro share GPX and use the content probe.
    private static readonly Dictionary<string, HashSet<Type>> ExplicitOverlaps = CreateExceptions();

    public static void Validate(IEnumerable<ICategory> categories)
    {
        var conflicts = categories
            .SelectMany(c => c.Extensions.Select(extension => (Extension: Normalize(extension), Category: c)))
            .GroupBy(x => x.Extension, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !IsExplicit(g.Key, g.Select(x => x.Category.GetType()).ToArray()))
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Category.Name).OrderBy(n => n))}")
            .ToArray();

        if (conflicts.Length > 0)
            throw new InvalidOperationException($"Category extensions overlap: {string.Join("; ", conflicts)}");
    }

    private static bool IsExplicit(string extension, Type[] types) =>
        types.Length == 2 && ExplicitOverlaps.TryGetValue(extension, out var expected) && expected.SetEquals(types);

    private static string Normalize(string extension)
    {
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    private static Dictionary<string, HashSet<Type>> CreateExceptions()
    {
        // Each extension is named so a new image extension requires an explicit decision as well.
        string[] sharedImages = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".raw",
            ".cr2", ".cr3", ".nef", ".arw", ".orf", ".sr2", ".dng", ".rw2", ".pef", ".raf",
            ".srw", ".k25", ".webp", ".heic"];
        var allowed = sharedImages.ToDictionary(extension => extension,
            _ => new HashSet<Type> { typeof(ImagesCategory), typeof(PhotosCategory) },
            StringComparer.OrdinalIgnoreCase);
        allowed.Add(".gpx", [typeof(GpsTracksCategory), typeof(MusicalInstrumentsCategory)]);
        return allowed;
    }
}
