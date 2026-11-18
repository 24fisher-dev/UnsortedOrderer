namespace UnsortedOrderer.Categories;

public sealed class ArchivesCategory : FileCategory
{
    private static readonly string[] ArchiveExtensions =
    [
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".zst"
    ];

    public ArchivesCategory(string folderName)
        : base("Archives", folderName, ArchiveExtensions)
    {
    }
}
