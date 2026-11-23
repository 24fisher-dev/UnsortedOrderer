using Microsoft.Extensions.DependencyInjection;
using UnsortedOrderer.Application.Application;
using UnsortedOrderer.Application.DependencyInjection;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class PrintingProjectFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "source");
    private string Destination => Path.Combine(_root, "destination");

    [Fact]
    public void Models_in_one_folder_stay_together_in_year_month_project_folder()
    {
        var first = Put("Bike mount/left.stl", "left");
        Put("Bike mount/right.3mf", "right");
        var month = DateFolder(first);

        Sort();

        Assert.Equal("left", File.ReadAllText(Path.Combine(month, "Bike mount", "left.stl")));
        Assert.Equal("right", File.ReadAllText(Path.Combine(month, "Bike mount", "right.3mf")));
        Assert.False(Directory.Exists(Path.Combine(Source, "Bike mount")));
    }

    [Fact]
    public void Print_project_keeps_previews_material_settings_and_nested_files()
    {
        var model = Put("Adapter/model.3mf", "model");
        Put("Adapter/preview.png", "preview");
        Put("Adapter/settings.json", "{ \"project\": true }");
        Put("Adapter/docs/notes.pdf", "notes");
        var month = DateFolder(model);

        Sort();

        Assert.Equal("model", File.ReadAllText(Path.Combine(month, "Adapter", "model.3mf")));
        Assert.Equal("preview", File.ReadAllText(Path.Combine(month, "Adapter", "preview.png")));
        Assert.Equal("{ \"project\": true }", File.ReadAllText(Path.Combine(month, "Adapter", "settings.json")));
        Assert.Equal("notes", File.ReadAllText(Path.Combine(month, "Adapter", "docs", "notes.pdf")));
    }

    [Fact]
    public void Mixed_or_executable_folders_are_not_moved_as_print_projects()
    {
        Put("Mixed/model.stl", "model");
        Put("Mixed/automation.bat", "script");
        Put("Installer/model.3mf", "model");
        Put("Installer/setup.exe", "installer");
        Sort();

        Assert.False(Directory.Exists(Path.Combine(Destination, "3DPrinting", "Mixed")));
        Assert.True(Directory.EnumerateFiles(Path.Combine(Destination, "3DPrinting"), "model.stl",
            SearchOption.AllDirectories).Any());
        Assert.True(File.Exists(Path.Combine(Destination, "Soft", "setup.exe")) ||
                    Directory.EnumerateFiles(Path.Combine(Destination, "Soft"), "setup.exe",
                        SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void Parent_container_is_not_swallowed_when_it_contains_two_projects()
    {
        Put("Collection/Project A/part.stl", "a");
        Put("Collection/Project A/readme.txt", "a notes");
        Put("Collection/Project B/part.stl", "b");
        Put("Collection/Project B/readme.txt", "b notes");
        Sort();
        var projects = Directory.EnumerateDirectories(Path.Combine(Destination, "3DPrinting"),
            "Project *", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
        Assert.Contains("Project A", projects);
        Assert.Contains("Project B", projects);
        Assert.DoesNotContain("Collection", projects);
    }

    [Fact]
    public void Existing_project_directory_is_not_overwritten()
    {
        var first = Put("Bike mount/left.stl", "new");
        Put("Bike mount/right.stl", "other");
        var month = DateFolder(first);
        Directory.CreateDirectory(Path.Combine(month, "Bike mount"));
        File.WriteAllText(Path.Combine(month, "Bike mount", "left.stl"), "old");

        Sort();

        Assert.Equal("old", File.ReadAllText(Path.Combine(month, "Bike mount", "left.stl")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(month, "Bike mount(1)", "left.stl")));
        Assert.Equal("other", File.ReadAllText(Path.Combine(month, "Bike mount(1)", "right.stl")));
    }

    [Fact]
    public void Single_model_is_still_sorted_as_an_individual_file()
    {
        var first = Put("One model/part.stl", "model");
        var month = DateFolder(first);
        Sort();
        Assert.Equal("model", File.ReadAllText(Path.Combine(month, "part.stl")));
    }

    private string DateFolder(string file)
    {
        var date = File.GetCreationTime(file);
        if (date.Year < 1980) date = File.GetLastWriteTime(file);
        return Path.Combine(Destination, "3DPrinting", date.ToString("yyyy"), date.ToString("MM"));
    }

    private string Put(string relative, string content)
    {
        var path = Path.Combine(Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private void Sort()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IMessageWriter>(new NullLog())
            .AddUnsortedOrdererApplication(new AppSettings(Source, Destination, "Soft", "Archives",
                "Images", "Photos", "Music", "Guitar Tabs", "EBooks", "Repositories", "Drivers",
                "Firmware", "Metadata", "Web", "Graphics", "Unknown", [], [], [], []))
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
