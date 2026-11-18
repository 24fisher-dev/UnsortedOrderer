namespace UnsortedOrderer.Models;

public sealed class ApplicationLocation
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private readonly string _baseDirectory;
    private readonly string? _executable;
    private readonly string _settings;

    public ApplicationLocation(string baseDirectory, string? executable)
    {
        _baseDirectory = Normalize(baseDirectory);
        _executable = string.IsNullOrWhiteSpace(executable) ? null : Normalize(executable);
        _settings = Path.Combine(_baseDirectory, "appsettings.json");
    }

    public static ApplicationLocation Current { get; } = new(AppContext.BaseDirectory, Environment.ProcessPath);

    public bool IsInside(string directory) => ContainsPath(directory, _baseDirectory);

    public bool ProtectsFile(string file)
    {
        var fullPath = Normalize(file);
        return string.Equals(fullPath, _executable, PathComparison)
            || string.Equals(fullPath, _settings, PathComparison);
    }

    public bool ProtectsDirectory(string directory) =>
        ContainsPath(directory, _baseDirectory)
        || (_executable is not null && ContainsPath(directory, _executable));

    internal static bool ContainsPath(string directory, string path)
    {
        var root = Normalize(directory);
        var target = Normalize(path);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(target, root, PathComparison) || target.StartsWith(prefix, PathComparison);
    }

    internal static bool SamePath(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), PathComparison);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
