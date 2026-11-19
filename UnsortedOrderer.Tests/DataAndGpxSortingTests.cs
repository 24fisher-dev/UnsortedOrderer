using System.Text;
using System.IO.Compression;
using System.Reflection;
using UnsortedOrderer.Services;
using Microsoft.Extensions.DependencyInjection;
using UnsortedOrderer.Application.Application;
using UnsortedOrderer.Application.DependencyInjection;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class DataAndGpxSortingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "source");
    private string Destination => Path.Combine(_root, "destination");

    [Theory]
    [InlineData("BCFSexample", "Guitar Tabs")]
    [InlineData("BCFZexample", "Guitar Tabs")]
    [InlineData("<gpx xmlns='http://www.topografix.com/GPX/1/1'><trk/></gpx>", "GPS Tracks")]
    [InlineData("<gps:gpx xmlns:gps='http://www.topografix.com/GPX/1/0'><gps:rte/></gps:gpx>", "GPS Tracks")]
    [InlineData("<gpx><metadata/><trk><trkseg/></trk></gpx>", "GPS Tracks")]
    [InlineData("<gpx xmlns='urn:other'><trk/></gpx>", "Unknown/gpx")]
    [InlineData("<gpx><notATrack/></gpx>", "Unknown/gpx")]
    [InlineData("<!DOCTYPE gpx SYSTEM 'https://example.invalid/entity'><gpx><trk/></gpx>", "Unknown/gpx")]
    [InlineData("BCF", "Unknown/gpx")]
    [InlineData("not a GPX", "Unknown/gpx")]
    public void Gpx_route_is_based_on_the_format(string content, string expectedFolder)
    {
        var original = Put("ride.GPX", content);
        Sort();
        Assert.False(File.Exists(original));
        Assert.Equal(content, File.ReadAllText(Path.Combine(Destination, expectedFolder, "ride.GPX")));
    }

    private const string KmlTrack = """
        <kml xmlns="http://www.opengis.net/kml/2.2">
          <Document><Placemark><LineString><coordinates>37.5,56.2 37.6,56.3</coordinates></LineString></Placemark>
          <Placemark><Point><coordinates>37.5,56.2</coordinates></Point></Placemark></Document>
        </kml>
        """;
    private const string KmlMap = """
        <kml xmlns="http://www.opengis.net/kml/2.2"><Placemark><Polygon><outerBoundaryIs>
        <LinearRing><coordinates>37,56 38,56 38,57 37,56</coordinates></LinearRing>
        </outerBoundaryIs></Polygon></Placemark></kml>
        """;

    [Theory]
    [InlineData("Дмитров_Городское_поселение_Деденево_1.kml", KmlTrack, "GPS Tracks")]
    [InlineData("boundary.kml", KmlMap, "Maps")]
    [InlineData("point.kml", "<kml><Placemark><Point/></Placemark></kml>", "Maps")]
    [InlineData("gx.kml", "<kml xmlns:gx='http://www.google.com/kml/ext/2.2'><Placemark><gx:MultiTrack><gx:Track><gx:coord>37 56 0</gx:coord></gx:Track></gx:MultiTrack></Placemark></kml>", "GPS Tracks")]
    [InlineData("corrupt.kml", "<kml><LineString>broken", "Maps")]
    [InlineData("untrusted.kml", "<!DOCTYPE kml SYSTEM 'https://example.invalid/dtd'><kml/>", "Maps")]
    [InlineData("ride.tcx", "<TrainingCenterDatabase xmlns='http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2'><Track><Trackpoint><Position><LatitudeDegrees>56</LatitudeDegrees><LongitudeDegrees>37</LongitudeDegrees></Position></Trackpoint></Track></TrainingCenterDatabase>", "GPS Tracks")]
    [InlineData("indoor.tcx", "<TrainingCenterDatabase xmlns='http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2'><Track><Trackpoint><HeartRateBpm/></Trackpoint></Track></TrainingCenterDatabase>", "Maps")]
    public void Track_containers_are_routed_by_content(string name, string content, string expected)
    {
        Put(name, content);
        Sort();
        Assert.Equal(content, File.ReadAllText(Path.Combine(Destination, expected, name)));
    }

    [Theory]
    [InlineData(KmlTrack, "GPS Tracks")]
    [InlineData(KmlMap, "Maps")]
    public void Kmz_uses_its_primary_kml_without_extracting_files(string content, string expected)
    {
        Directory.CreateDirectory(Source);
        var file = Path.Combine(Source, "bundle.kmz");
        using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            using (var document = new StreamWriter(archive.CreateEntry("doc.kml").Open()))
                document.Write(content);
            using (var extra = new StreamWriter(archive.CreateEntry("assets/other.kml").Open()))
                extra.Write(KmlTrack);
        }
        Sort();
        Assert.True(File.Exists(Path.Combine(Destination, expected, "bundle.kmz")));
        Assert.False(Directory.Exists(Path.Combine(Destination, expected, "assets")));
    }

    [Fact]
    public void Legacy_gpx_and_sorted_map_tracks_are_migrated_without_replacing_existing_files()
    {
        var gps = Directory.CreateDirectory(Path.Combine(Destination, "GPS Tracks")).FullName;
        var old = Directory.CreateDirectory(Path.Combine(Destination, "GPX")).FullName;
        var maps = Directory.CreateDirectory(Path.Combine(Destination, "Maps")).FullName;
        File.WriteAllText(Path.Combine(gps, "ride.gpx"), "existing");
        File.WriteAllText(Path.Combine(old, "ride.gpx"), "legacy");
        File.WriteAllText(Path.Combine(maps, "Дмитров_Городское_поселение_Деденево_1.kml"), KmlTrack);
        File.WriteAllText(Path.Combine(maps, "boundary.kml"), KmlMap);

        Sort();
        Sort();
        Assert.False(Directory.Exists(old));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(gps, "ride.gpx")));
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(gps, "ride(1).gpx")));
        Assert.Equal(KmlTrack, File.ReadAllText(Path.Combine(gps, "Дмитров_Городское_поселение_Деденево_1.kml")));
        Assert.False(File.Exists(Path.Combine(maps, "Дмитров_Городское_поселение_Деденево_1.kml")));
        Assert.Equal(KmlMap, File.ReadAllText(Path.Combine(maps, "boundary.kml")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_archive_stays_with_the_kml_track_in_either_processing_order(bool archiveFirst)
    {
        var archive = Put("path.zip", "map resources");
        var kml = Put("path.kml", KmlTrack);
        using var provider = BuildProvider();
        var sorter = provider.GetRequiredService<FileOrganizerService>();
        var processFile = typeof(FileOrganizerService).GetMethod("ProcessFile", BindingFlags.NonPublic | BindingFlags.Instance)!;

        processFile.Invoke(sorter, [archiveFirst ? archive : kml]);
        processFile.Invoke(sorter, [archiveFirst ? kml : archive]);

        var tracks = Path.Combine(Destination, "GPS Tracks");
        Assert.Equal(KmlTrack, File.ReadAllText(Path.Combine(tracks, "path.kml")));
        Assert.Equal("map resources", File.ReadAllText(Path.Combine(tracks, "path.zip")));
    }

    [Fact]
    public void Xml_data_folder_is_moved_whole_without_overwriting_existing_dataset()
    {
        Put("November/80020.xml", "<message><day>18</day></message>");
        var sourceXml = Put("November/80021.XML", "<measurements/>");
        Put("November/total.csv", "day,total\n18,25");
        var previous = Path.Combine(Destination, "Data", "November");
        Directory.CreateDirectory(previous);
        File.WriteAllText(Path.Combine(previous, "80020.xml"), "old");

        Sort();

        var dataset = Path.Combine(Destination, "Data", "November(1)");
        Assert.False(Directory.Exists(Path.GetDirectoryName(sourceXml)));
        Assert.Equal("old", File.ReadAllText(Path.Combine(previous, "80020.xml")));
        Assert.Equal("<measurements/>", File.ReadAllText(Path.Combine(dataset, "80021.XML")));
        Assert.True(File.Exists(Path.Combine(dataset, "80020.xml")));
        Assert.True(File.Exists(Path.Combine(dataset, "total.csv")));
    }

    [Fact]
    public void Xml_does_not_reclassify_software_packages_or_mixed_directories()
    {
        Put("Driver package/device.inf", "driver");
        Put("Driver package/layout.xml", "<data/>");
        Put("setup-bundle/setup.exe", "installer");
        Put("setup-bundle/settings.xml", "<configuration/>");
        Put("mixed/notes.pdf", "notes");
        Put("mixed/data.xml", "<data/>");
        Put("settings-only/prefs.xml", "<settings/>");
        Put("bad-xml/broken.xml", "not xml");

        Sort();

        Assert.True(File.Exists(Path.Combine(Destination, "Drivers", "Driver package", "layout.xml")));
        Assert.True(File.Exists(Path.Combine(Destination, "Soft", "setup-bundle", "settings.xml")));
        Assert.True(File.Exists(Path.Combine(Destination, "Documents", "notes.pdf")));
        Assert.False(Directory.Exists(Path.Combine(Destination, "Data")));
        Assert.True(File.Exists(Path.Combine(Destination, "Soft", "_Xml_files", "prefs.xml")));
    }

    [Theory]
    [InlineData("grid.XLS", "Data")]
    [InlineData("grid.xlsx", "Data")]
    [InlineData("grid.xlsm", "Data")]
    [InlineData("grid.xlsb", "Data")]
    [InlineData("grid.csv", "Data")]
    [InlineData("grid.ods", "Data")]
    [InlineData("model.f3d", "3DPrinting")]
    [InlineData("model.skp", "3DPrinting")]
    [InlineData("vector.svg", "Graphics")]
    [InlineData("sticker.tgs", "Graphics")]
    [InlineData("preview.thm", "Metadata")]
    [InlineData("component.winmd", "Metadata")]
    [InlineData("script.scr", "Unknown/scr")]
    [InlineData("dump.data", "Unknown/data")]
    [InlineData("dump.json", "Data")]
    [InlineData("font.woff2", "Fonts")]
    [InlineData("diagram.drawio", "Diagrams")]
    [InlineData("database.sqlite3", "Databases")]
    [InlineData("map.kmz", "Maps")]
    [InlineData("project.mpp", "ProjectPlans")]
    [InlineData("table.parquet", "Data")]
    [InlineData("drawing.dxf", "3DPrinting")]
    [InlineData("resin.photon", "3DPrinting")]
    [InlineData("cura.curaprofile", "3DPrinting")]
    [InlineData("sketch.ino", "Repositories")]
    [InlineData("bios.rom", "ROMs")]
    [InlineData("unfinished.download", "IncompleteDownloads")]
    [InlineData("unclear.parts", "Unknown/parts")]
    [InlineData("spool.prn", "Unknown/prn")]
    [InlineData("README", "Unknown/no-extension")]
    public void Known_extensions_are_classified_and_ambiguous_ones_are_preserved(string name, string folder)
    {
        Put(name, "not an executable");
        Sort();
        Assert.True(folder == "3DPrinting"
            ? Directory.EnumerateFiles(Path.Combine(Destination, folder), name, SearchOption.AllDirectories).Any()
            : File.Exists(Path.Combine(Destination, folder, name)));
    }

    [Fact]
    public void Windows_screensaver_is_routed_as_software_without_running_it()
    {
        var path = Put("saver.scr", "");
        File.Copy(typeof(DataAndGpxSortingTests).Assembly.Location, path, overwrite: true);
        Sort();
        Assert.Single(Directory.EnumerateFiles(Path.Combine(Destination, "Soft"), "saver.scr", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("BCFSscore", "Guitar Tabs", "GPS Tracks", true)]
    [InlineData("BCFSscore", "Guitar Tabs", "GPS Tracks", false)]
    [InlineData("<gpx><trk/></gpx>", "GPS Tracks", "Guitar Tabs", true)]
    [InlineData("<gpx><trk/></gpx>", "GPS Tracks", "Guitar Tabs", false)]
    public void Archive_follows_current_gpx_not_older_same_name_in_both_orders(string content, string folder, string oldFolder, bool archiveFirst)
    {
        var archive = Put("route.zip", "new archive");
        var gpx = Put("route.gpx", content);
        var old = Path.Combine(Destination, oldFolder);
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "route.zip"), "old archive");
        using var provider = BuildProvider();

        var sorter = provider.GetRequiredService<FileOrganizerService>();
        var processFile = typeof(FileOrganizerService).GetMethod("ProcessFile", BindingFlags.NonPublic | BindingFlags.Instance)!;
        processFile.Invoke(sorter, [archiveFirst ? archive : gpx]);
        processFile.Invoke(sorter, [archiveFirst ? gpx : archive]);
        provider.GetRequiredService<FileOrganizerApplication>().Run();

        Assert.Equal(content, File.ReadAllText(Path.Combine(Destination, folder, "route.gpx")));
        Assert.Equal("new archive", File.ReadAllText(Path.Combine(Destination, folder, "route.zip")));
        Assert.Equal("old archive", File.ReadAllText(Path.Combine(old, "route.zip")));
    }

    [Fact]
    public void Software_and_driver_bundles_do_not_become_firmware_or_repositories()
    {
        Put("AIDA64 Extreme 6.20 RePack/payload/aida64.exe", "program");
        Put("AIDA64 Extreme 6.20 RePack/install.bin", "install data");
        Put("RealtekALC883_Audio_V5100591/Setup.exe", "driver installer");
        Put("RealtekALC883_Audio_V5100591/payload.dat", "driver payload");
        Put("Photoshop 2126 Lite/pack/Photoshop.exe", "program");
        Put("Photoshop 2126 Lite/package.json", "{}");
        foreach (var name in new[] { "a.js", "b.js", "c.js" })
            Put($"Photoshop 2126 Lite/scripts/{name}", "component");
        Put("genuine firmware/flash.exe", "updater");
        Put("genuine firmware/device.bin", "firmware");
        Put("Photoshop plugin experiment/.git/HEAD", "ref");
        Put("Photoshop plugin experiment/Photoshop.exe", "fixture");
        Put("KN-1210_firmware.bin", "router firmware");

        Sort();

        Assert.True(File.Exists(Path.Combine(Destination, "Soft", "AIDA64 Extreme 6.20 RePack", "install.bin")));
        Assert.True(File.Exists(Path.Combine(Destination, "Drivers", "RealtekALC883_Audio_V5100591", "payload.dat")));
        Assert.True(File.Exists(Path.Combine(Destination, "Soft", "Photoshop 2126 Lite", "package.json")));
        Assert.True(File.Exists(Path.Combine(Destination, "Repositories", "Photoshop plugin experiment", ".git", "HEAD")));
        Assert.True(File.Exists(Path.Combine(Destination, "Firmware", "genuine firmware", "flash.exe")));
        Assert.True(File.Exists(Path.Combine(Destination, "Firmware", "KN-1210_firmware.bin")));
    }

    [Fact]
    public void Legacy_sorted_folders_are_consolidated_without_overwriting_existing_files()
    {
        var oldModel = PutDestination("3DModels/thing.obj", "previous 3D file");
        var date = File.GetCreationTime(oldModel);
        var datedFolder = Path.Combine("3DPrinting", date.Year.ToString("D4"), date.Month.ToString("D2"));
        PutDestination(Path.Combine(datedFolder, "thing.obj"), "already present");
        PutDestination("Instruments/tab.gp", "previous tab");
        PutDestination("Guitar Tabs/tab.gp", "already present");
        PutDestination("Firmware/AIDA64 Extreme RePack/payload/aida64.exe", "program");
        PutDestination("Firmware/AIDA64 Extreme RePack/payload.dat", "binary");
        PutDestination("Firmware/RealtekALC883_Audio_V5100591/a.bin", "driver binary");
        PutDestination("Firmware/KN-1210_firmware.bin", "keep");
        PutDestination("Repositories/Photoshop 2126 Lite/Photoshop.exe", "program");
        PutDestination("Repositories/Photoshop 2126 Lite/js/package.json", "{}");
        PutDestination("Repositories/Photoshop plugins/.git/HEAD", "keep");
        PutDestination("Repositories/Photoshop plugins/Photoshop.exe", "keep");
        PutDestination("Firmware/device firmware/flash.exe", "keep firmware");
        PutDestination("Firmware/device firmware/board.bin", "keep firmware");

        Sort();
        Sort();

        Assert.False(Directory.Exists(Path.Combine(Destination, "3DModels")));
        Assert.False(Directory.Exists(Path.Combine(Destination, "Instruments")));
        Assert.Equal("already present", File.ReadAllText(Path.Combine(Destination, datedFolder, "thing.obj")));
        Assert.Equal("previous 3D file", File.ReadAllText(Path.Combine(Destination, datedFolder, "thing(1).obj")));
        Assert.Equal("already present", File.ReadAllText(Path.Combine(Destination, "Guitar Tabs", "tab.gp")));
        Assert.Equal("previous tab", File.ReadAllText(Path.Combine(Destination, "Guitar Tabs", "tab(1).gp")));
        Assert.True(File.Exists(Path.Combine(Destination, "Soft", "Photoshop 2126 Lite", "Photoshop.exe")));
        Assert.True(File.Exists(Path.Combine(Destination, "Soft", "AIDA64 Extreme RePack", "payload.dat")));
        Assert.True(File.Exists(Path.Combine(Destination, "Drivers", "RealtekALC883_Audio_V5100591", "a.bin")));
        Assert.True(File.Exists(Path.Combine(Destination, "Firmware", "KN-1210_firmware.bin")));
        Assert.True(File.Exists(Path.Combine(Destination, "Firmware", "device firmware", "board.bin")));
        Assert.True(File.Exists(Path.Combine(Destination, "Repositories", "Photoshop plugins", ".git", "HEAD")));
    }

    private string PutDestination(string relativePath, string contents)
    {
        var path = Path.Combine(Destination, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private string Put(string relativePath, string contents)
    {
        var path = Path.Combine(Source, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        return path;
    }

    private void Sort()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<FileOrganizerApplication>().Run();
    }

    private ServiceProvider BuildProvider() => new ServiceCollection()
            .AddSingleton<IMessageWriter>(new NullLog())
            .AddUnsortedOrdererApplication(new AppSettings(Source, Destination, "Soft", "Archives",
                "Images", "Photos", "Music", "Guitar Tabs", "EBooks", "Repositories", "Drivers", "Firmware",
                "Metadata", "Web", "Graphics", "Unknown", [], [], [], []))
            .BuildServiceProvider();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class NullLog : IMessageWriter
    {
        public void WriteLine(string message) { }
    }
}
