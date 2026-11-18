using System.Text.Json;

namespace UnsortedOrderer.Application.Services.Categories;

internal static class FilamentProfileDetector
{
    private const int MaxProfileBytes = 1024 * 1024;

    public static bool IsMatch(string path)
    {
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > MaxProfileBytes)
            {
                return false;
            }

            using var reader = new StreamReader(stream);
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var hasMaterialType = HasText(root, "filament_type");
            var hasPresetId = HasText(root, "filament_settings_id");
            var hasMaterialId = HasText(root, "filament_id");
            var inherits = HasText(root, "inherits");
            if (root.TryGetProperty("type", out var type))
            {
                return type.ValueKind == JsonValueKind.String
                    && type.GetString() == "filament"
                    && (hasMaterialType || hasPresetId || hasMaterialId || inherits);
            }

            // Some exported user presets omit the type discriminator.
            return (hasPresetId && (hasMaterialType || hasMaterialId || inherits))
                || (hasMaterialId && hasMaterialType);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasText(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && (IsText(value) || value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(IsText));

    private static bool IsText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString());
}
