namespace UnsortedOrderer.Categories;

public sealed class GraphicsCategory : FileCategory
{
    private static readonly string[] GraphicsExtensions =
    [
        ".psd",
        ".aseprite", ".xd", ".sai2", ".xbm",
        ".svg",
        ".tgs",
        ".psb",
        ".ai",
        ".eps",
        ".xcf",
        ".kra",
        ".ora",
        ".clip",
        ".afphoto",
        ".afdesign",
        ".afpub",
        ".cdr",
        ".sai",
        ".mdp",
        ".pdn",
        ".pspimage",
        ".reb",
        ".sketch",
        ".fig"
    ];

    public GraphicsCategory(string folderName)
        : base("Graphics", folderName, GraphicsExtensions)
    {
    }
}
