using System.Text;
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
    [InlineData("BCFSexample", "Instruments")]
    [InlineData("BCFZexample", "Instruments")]
    [InlineData("<gpx xmlns='http://www.topografix.com/GPX/1/1'><trk/></gpx>", "GPX")]
    [InlineData("<gps:gpx xmlns:gps='http://www.topografix.com/GPX/1/0'><gps:rte/></gps:gpx>", "GPX")]
    [InlineData("<gpx><metadata/><trk><trkseg/></trk></gpx>", "GPX")]
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
    [InlineData("model.f3d", "3DModels")]
    [InlineData("model.skp", "3DModels")]
    [InlineData("vector.svg", "Graphics")]
    [InlineData("sticker.tgs", "Graphics")]
    [InlineData("preview.thm", "Metadata")]
    [InlineData("component.winmd", "Metadata")]
    [InlineData("script.scr", "Unknown/scr")]
    [InlineData("dump.data", "Unknown/data")]
    [InlineData("dump.json", "Unknown/json")]
    [InlineData("spool.prn", "Unknown/prn")]
    [InlineData("README", "Unknown/no-extension")]
    public void Known_extensions_are_classified_and_ambiguous_ones_are_preserved(string name, string folder)
    {
        Put(name, "not an executable");
        Sort();
        Assert.True(File.Exists(Path.Combine(Destination, folder, name)));
    }

    [Fact]
    public void Windows_screensaver_is_routed_as_software_without_running_it()
    {
        var path = Put("saver.scr", "");
        File.Copy(typeof(DataAndGpxSortingTests).Assembly.Location, path, overwrite: true);
        Sort();
        Assert.Single(Directory.EnumerateFiles(Path.Combine(Destination, "Soft"), "saver.scr", SearchOption.AllDirectories));
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
        using var provider = new ServiceCollection()
            .AddSingleton<IMessageWriter>(new NullLog())
            .AddUnsortedOrdererApplication(new AppSettings(Source, Destination, "Soft", "Archives",
                "Images", "Photos", "Music", "Instruments", "EBooks", "Repositories", "Drivers", "Firmware",
                "Metadata", "Web", "Graphics", "Unknown", [], [], [], []))
            .BuildServiceProvider();
        provider.GetRequiredService<FileOrganizerApplication>().Run();
    }

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
