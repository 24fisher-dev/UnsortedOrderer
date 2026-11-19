namespace UnsortedOrderer.Categories;

public sealed class ThreeDModelsCategory : FileCategory
{
    private static readonly string[] ModelExtensions =
    [
        ".obj",
        ".f3d",
        ".skp",
        ".fbx",
        ".3ds",
        ".dae",
        ".ply",
        ".blend",
        ".gltf",
        ".glb",
        ".usdz",
        ".step",
        ".stp",
        ".iges",
        ".igs",
        ".scad"
    ];

    public ThreeDModelsCategory()
        : base("3DModels", "3DModels", ModelExtensions)
    {
    }
}
