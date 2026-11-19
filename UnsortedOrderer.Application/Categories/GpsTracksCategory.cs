namespace UnsortedOrderer.Categories;

// GPX is shared with Guitar Pro. Only files identified by the content probe are routed here.
public sealed class GpsTracksCategory() : FileCategory(Folder, Folder, [".gpx"])
{
    public const string Folder = "GPS Tracks";
}
