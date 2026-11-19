using Microsoft.Extensions.DependencyInjection;
using UnsortedOrderer.Application.Application;
using UnsortedOrderer.Application.DependencyInjection;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class CleanupRunTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "unsorted");
    private string Destination => Path.Combine(_root, "sorted");

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    public void Run_imports_only_selected_and_available_folders_before_sorting(
        bool desktopSelected, bool downloadsSelected, bool available)
    {
        var desktop = new CleanupStub("desktop.txt", available);
        var downloads = new CleanupStub("download.txt", available);
        var stages = new List<CleanupStage>();
        using var provider = BuildRun(desktop, downloads, new Log());
        provider.GetRequiredService<FileOrganizerApplication>().Run(desktopSelected, downloadsSelected, stages.Add);

        var expected = new List<CleanupStage>();
        if (desktopSelected && available)
            expected.Add(CleanupStage.Desktop);
        if (downloadsSelected && available)
            expected.Add(CleanupStage.Downloads);
        expected.Add(CleanupStage.Sorting);
        Assert.Equal(expected, stages);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Source));
        Assert.Equal(desktopSelected && available, HasSortedFile("desktop.txt"));
        Assert.Equal(downloadsSelected && available, HasSortedFile("download.txt"));
    }

    [Fact]
    public void Common_desktop_requires_explicit_selection_and_reports_failures()
    {
        var desktop = new CleanupStub("desktop.txt");
        using var provider = BuildRun(desktop, new CleanupStub("download.txt"), new Log());
        var app = provider.GetRequiredService<FileOrganizerApplication>();

        Assert.Equal(0, app.Run(cleanDesktop: true));
        Assert.Equal(0, desktop.CommonCalls);
        Assert.Equal(1, app.Run(cleanCommonDesktop: true));
        Assert.Equal(1, desktop.CommonCalls);
    }

    [Fact]
    public void Files_reported_on_the_common_desktop_reach_sorted_categories_when_enabled()
    {
        var personal = Directory.CreateDirectory(Path.Combine(_root, "Personal")).FullName;
        var shared = Directory.CreateDirectory(Path.Combine(_root, "Public")).FullName;
        var log = new Log();
        File.WriteAllText(Path.Combine(shared, "ipad.json"), "{}");
        File.WriteAllText(Path.Combine(shared, "cascade.conf"), "configuration");
        File.WriteAllText(Path.Combine(shared, "haven.zip"), "archive");
        File.WriteAllText(Path.Combine(shared, "automation.bat"), "@echo off");
        var executable = Path.Combine(shared, "UnsortedOrderer.exe");
        File.WriteAllText(executable, "running executable");
        var location = new ApplicationLocation(shared, executable);
        var desktop = new UnsortedOrderer.Services.DesktopCleanupService(log, personal, location, commonDesktopPath: shared);

        using (var provider = BuildRun(desktop, new CleanupStub("download.txt"), log, location))
            Assert.Equal(0, provider.GetRequiredService<FileOrganizerApplication>().Run(cleanDesktop: true));
        Assert.True(File.Exists(Path.Combine(shared, "ipad.json")));

        using (var provider = BuildRun(desktop, new CleanupStub("download.txt"), log, location))
            Assert.Equal(0, provider.GetRequiredService<FileOrganizerApplication>().Run(cleanDesktop: true, cleanCommonDesktop: true));

        Assert.Equal("{}", File.ReadAllText(Path.Combine(Destination, "Data", "ipad.json")));
        Assert.Equal("configuration", File.ReadAllText(Path.Combine(Destination, "Configurations", "cascade.conf")));
        Assert.Equal("archive", File.ReadAllText(Path.Combine(Destination, "Archives", "haven.zip")));
        Assert.Equal("@echo off", File.ReadAllText(Path.Combine(shared, "automation.bat")));
        Assert.True(File.Exists(executable));
        Assert.False(File.Exists(Path.Combine(shared, "ipad.json")));
        Assert.False(File.Exists(Path.Combine(shared, "cascade.conf")));
        Assert.False(File.Exists(Path.Combine(shared, "haven.zip")));
    }

    [Fact]
    public void A_new_run_does_not_keep_previous_statistics()
    {
        Directory.CreateDirectory(Source);
        File.WriteAllText(Path.Combine(Source, "example.txt"), "test");
        var firstLog = new Log();
        var nextLog = new Log();
        using (var first = BuildRun(new CleanupStub("desktop.txt"), new CleanupStub("download.txt"), firstLog))
            first.GetRequiredService<FileOrganizerApplication>().Run();
        using (var next = BuildRun(new CleanupStub("desktop.txt"), new CleanupStub("download.txt"), nextLog))
            next.GetRequiredService<FileOrganizerApplication>().Run();

        Assert.Contains("Total files moved: 1", firstLog.Lines);
        Assert.Contains("Total files moved: 0", nextLog.Lines);
        Assert.True(HasSortedFile("example.txt"));
    }

    [Fact]
    public void Import_failure_stops_before_sorting_and_allows_a_new_run()
    {
        Directory.CreateDirectory(Source);
        var sourceFile = Path.Combine(Source, "example.txt");
        File.WriteAllText(sourceFile, "test");
        var stages = new List<CleanupStage>();
        using (var failed = BuildRun(new CleanupStub("desktop.txt", fail: true), new CleanupStub("download.txt"), new Log()))
        {
            Assert.Throws<IOException>(() => failed.GetRequiredService<FileOrganizerApplication>().Run(true, true, stages.Add));
        }

        Assert.Equal(new[] { CleanupStage.Desktop }, stages);
        Assert.True(File.Exists(sourceFile));
        Assert.False(Directory.Exists(Destination));
        using var next = BuildRun(new CleanupStub("desktop.txt"), new CleanupStub("download.txt"), new Log());
        next.GetRequiredService<FileOrganizerApplication>().Run();
        Assert.True(HasSortedFile("example.txt"));
    }

    [Theory]
    [InlineData("mount.STL", "stl")]
    [InlineData("plate.3mf", "3mf")]
    [InlineData("plate.gcode", "gcode")]
    [InlineData("plate.bgcode", "bgcode")]
    [InlineData("NIT PLA Pastel.json", """{"type":"filament","name":"NIT PLA Pastel","inherits":"Generic PLA"}""")]
    [InlineData("custom.json", """{"filament_settings_id":["NIT PLA Pastel"],"filament_type":["PLA"]}""")]
    [InlineData("inherited.json", """{"filament_settings_id":["NIT PLA Pastel"],"inherits":"Generic PLA"}""")]
    public void Printing_files_go_to_creation_year_and_month(string name, string contents)
    {
        var original = WriteInput(name, contents);
        File.SetCreationTime(original, new DateTime(2024, 2, 5, 14, 30, 0));
        var date = File.GetCreationTime(original);

        RunSorting();

        var sorted = Path.Combine(Destination, "3DPrinting", date.Year.ToString("D4"), date.Month.ToString("D2"), name);
        Assert.True(File.Exists(sorted), sorted);
        Assert.Equal(contents, File.ReadAllText(sorted));
        Assert.False(File.Exists(original));
    }

    [Theory]
    [InlineData("""{"project":{"type":"filament","filament_type":["PLA"]}}""")]
    [InlineData("""{"type":"machine","filament_type":["PLA"],"filament_settings_id":["PLA"]}""")]
    [InlineData("""{"name":"NIT PLA Pastel","type":17}""")]
    [InlineData("""{"type":"filament"}""")]
    [InlineData("""{"type":"filament","name":"NIT PLA","inherits":"Generic PLA" """)]
    [InlineData("""[{"type":"filament","inherits":"Generic PLA"}]""")]
    public void Other_or_malformed_json_keeps_normal_sorting(string contents)
    {
        WriteInput("NIT PLA Pastel.json", contents);

        RunSorting();

        Assert.Equal(contents, File.ReadAllText(Path.Combine(Destination, "Data", "NIT PLA Pastel.json")));
        Assert.False(Directory.Exists(Path.Combine(Destination, "3DPrinting")));
    }

    [Fact]
    public void Crdownload_moves_to_quarantine_and_completed_file_is_sorted()
    {
        var partial = WriteInput("my document.pdf.CRDOWNLOAD", "incomplete");
        WriteInput("my document.pdf", "complete");

        RunSorting();

        Assert.False(File.Exists(partial));
        Assert.Equal("incomplete", File.ReadAllText(Path.Combine(Destination, "_ToDelete", "my document.pdf.CRDOWNLOAD")));
        Assert.Equal("complete", File.ReadAllText(Path.Combine(Destination, "Documents", "my document.pdf")));
    }

    [Fact]
    public void Quarantine_keeps_duplicate_names_and_retains_contents_on_repeat()
    {
        WriteInput("duplicate.tmp", "root");
        WriteInput(Path.Combine("nested", "duplicate.tmp"), "nested");
        WriteInput("another.tmp", "temp");

        RunSorting();
        RunSorting();

        var trash = Path.Combine(Destination, "_ToDelete");
        Assert.Equal("root", File.ReadAllText(Path.Combine(trash, "duplicate.tmp")));
        Assert.Equal("nested", File.ReadAllText(Path.Combine(trash, "duplicate(1).tmp")));
        Assert.Equal("temp", File.ReadAllText(Path.Combine(trash, "another.tmp")));
    }

    [Fact]
    public void Explicit_clear_removes_old_and_new_trash_but_not_other_files()
    {
        var oldTrash = Path.Combine(Destination, "_ToDelete", "old bundle");
        Directory.CreateDirectory(oldTrash);
        File.WriteAllText(Path.Combine(oldTrash, "old.exe"), "stash");
        WriteInput("partial.crdownload", "recent");
        WriteInput("report.txt", "keep");
        using var provider = BuildRun(new CleanupStub("desktop.txt"), new CleanupStub("download.txt"), new Log());
        var app = provider.GetRequiredService<FileOrganizerApplication>();
        Assert.Equal(0, app.Run());
        Assert.True(File.Exists(Path.Combine(oldTrash, "old.exe")));
        Assert.True(File.Exists(Path.Combine(Destination, "_ToDelete", "partial.crdownload")));

        app.ClearQuarantine();
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(Destination, "_ToDelete")));
        Assert.True(HasSortedFile("report.txt"));
    }

    [Fact]
    public void Failed_archive_move_is_logged_and_other_directories_are_sorted()
    {
        var failedArchive = WriteInput(Path.Combine("Archives", "course.zip"), "archive");
        WriteInput(Path.Combine("other", "report.txt"), "keep");
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "Archives"), "obstructing file");
        var trash = Path.Combine(Destination, "_ToDelete");
        Directory.CreateDirectory(trash);
        File.WriteAllText(Path.Combine(trash, "old.tmp"), "saved");
        var log = new Log();
        using var provider = BuildRun(new CleanupStub("desktop.txt"), new CleanupStub("download.txt"), log);

        var failures = provider.GetRequiredService<FileOrganizerApplication>().Run();

        Assert.Equal(1, failures);
        Assert.Contains(log.Lines, line => line.Contains(failedArchive, StringComparison.Ordinal));
        Assert.Equal("archive", File.ReadAllText(failedArchive));
        Assert.True(HasSortedFile("report.txt"));
        Assert.True(File.Exists(Path.Combine(trash, "old.tmp")));
    }

    [Fact]
    public void Windows_paths_with_forward_slashes_are_normalized()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var provider = BuildRun(new CleanupStub("desktop.txt"), new CleanupStub("download.txt"), new Log(),
            pathsWithForwardSlashes: true);
        var settings = provider.GetRequiredService<AppSettings>();

        Assert.Equal(Source, settings.SourceDirectory);
        Assert.Equal(Destination, settings.DestinationRoot);
    }

    [Fact]
    public void Quarantine_does_not_follow_directory_links()
    {
        if (OperatingSystem.IsWindows()) return; // Windows symlinks can require elevated privileges.
        var trash = Path.Combine(Destination, "_ToDelete");
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(trash);
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(trash, "keep.tmp"), "existing");
        Directory.CreateSymbolicLink(Path.Combine(trash, "link"), elsewhere);
        var outsideFile = Path.Combine(elsewhere, "keep.txt");
        File.WriteAllText(outsideFile, "outside");
        using var provider = BuildRun(new CleanupStub("desktop.txt"), new CleanupStub("download.txt"), new Log());

        Assert.Throws<IOException>(() => provider.GetRequiredService<FileOrganizerApplication>().ClearQuarantine());
        Assert.Equal("outside", File.ReadAllText(outsideFile));
        Assert.True(File.Exists(Path.Combine(trash, "keep.tmp")));
    }

    [Fact]
    public void Same_named_print_models_are_preserved_without_overwrite()
    {
        WriteInput("mount.stl", "first");
        WriteInput(Path.Combine("nested", "mount.stl"), "second");

        RunSorting();

        var files = Directory.GetFiles(Path.Combine(Destination, "3DPrinting"), "*.stl", SearchOption.AllDirectories);
        Assert.Equal(new[] { "first", "second" }, files.Select(File.ReadAllText).OrderBy(text => text));
    }

    [Theory]
    [InlineData("table.ods", "Data")]
    [InlineData("slides.odp", "Documents")]
    [InlineData("art.aseprite", "Graphics")]
    [InlineData("backup.tgz", "Archives")]
    [InlineData("backup.zst", "Archives")]
    public void Added_extensions_have_explicit_non_destructive_destinations(string name, string folder)
    {
        WriteInput(name, "test");
        RunSorting();
        Assert.True(File.Exists(Path.Combine(Destination, folder, name)));
    }

    [Fact]
    public void Standalone_conf_files_are_sorted_but_configuration_inside_installers_stays_with_the_package()
    {
        WriteInput("install.conf", "standalone");
        WriteInput(Path.Combine("Bundle", "setup.exe"), "installer");
        WriteInput(Path.Combine("Bundle", "settings.conf"), "part of bundle");

        RunSorting();

        Assert.Equal("standalone", File.ReadAllText(Path.Combine(Destination, "Configurations", "install.conf")));
        Assert.Equal("part of bundle", File.ReadAllText(Path.Combine(Destination, "Soft", "Bundle", "settings.conf")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Organizing_a_source_containing_the_app_keeps_only_the_running_installation(bool nested)
    {
        var relativeDirectory = nested ? Path.Combine("portable", "current") : "";
        var app = WriteInput(Path.Combine(relativeDirectory, "Renamed release.exe"), "running");
        var config = WriteInput(Path.Combine(relativeDirectory, "appsettings.json"), "{}");
        var old = WriteInput("UnsortedOrderer.Console.exe", "old version");
        var additionalInput = WriteInput(Path.Combine("portable-old", "manual.txt"), "manual");
        var location = new ApplicationLocation(Path.GetDirectoryName(app)!, app);
        using var provider = BuildRun(new CleanupStub("desktop.txt"), new CleanupStub("download.txt"), new Log(), location);

        provider.GetRequiredService<FileOrganizerApplication>().Run();

        Assert.Equal("running", File.ReadAllText(app));
        Assert.Equal("{}", File.ReadAllText(config));
        Assert.True(HasSortedFile("UnsortedOrderer.Console.exe"));
        Assert.True(HasSortedFile("manual.txt"));
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(additionalInput));
    }

    private string WriteInput(string name, string contents)
    {
        var path = Path.Combine(Source, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private void RunSorting()
    {
        using var provider = BuildRun(new CleanupStub("desktop.txt"), new CleanupStub("download.txt"), new Log());
        provider.GetRequiredService<FileOrganizerApplication>().Run();
    }

    private bool HasSortedFile(string name) =>
        Directory.EnumerateFiles(Destination, name, SearchOption.AllDirectories).Any();

    private ServiceProvider BuildRun(IDesktopCleanupService desktop, IDownloadsCleanupService downloads, Log log,
        ApplicationLocation? application = null, bool pathsWithForwardSlashes = false) =>
        new ServiceCollection()
            .AddSingleton<IMessageWriter>(log)
            .AddSingleton(application ?? ApplicationLocation.Current)
            .AddUnsortedOrdererApplication(new AppSettings(
                pathsWithForwardSlashes ? Source.Replace(Path.DirectorySeparatorChar, '/') : Source,
                pathsWithForwardSlashes ? Destination.Replace(Path.DirectorySeparatorChar, '/') : Destination,
                "Soft", "Archives",
                "Images", "Photos", "Music", "Instruments", "EBooks", "Repositories", "Drivers", "Firmware",
                "Metadata", "Web", "Graphics", "Unknown", [".ini", ".tmp"], [], [], []))
            .AddSingleton(desktop)
            .AddSingleton(downloads)
            .BuildServiceProvider();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class CleanupStub(string name, bool available = true, bool fail = false)
        : IDesktopCleanupService, IDownloadsCleanupService
    {
        public bool CanClean(string destinationPath) => available;

        public int Clean(string destinationPath, string? sortedPath)
        {
            if (!CanClean(destinationPath))
                return 0;
            Clean(destinationPath);
            return 0;
        }

        public int CleanSelected(string destinationPath, string? sortedPath, bool personal, bool common) =>
            (personal ? Clean(destinationPath, sortedPath) : 0) + (common ? CleanCommon(destinationPath, sortedPath) : 0);

        public int CommonCalls { get; private set; }
        public int CleanCommon(string destinationPath, string? sortedPath)
        {
            CommonCalls++;
            return 1;
        }

        public void CreateFolderShortcuts(string unsortedPath, string sortedPath) { }

        public void Clean(string destinationPath)
        {
            if (fail)
                throw new IOException("Import failure");

            Directory.CreateDirectory(destinationPath);
            File.WriteAllText(Path.Combine(destinationPath, name), name);
        }
    }

    private sealed class Log : IMessageWriter
    {
        public List<string> Lines { get; } = new();
        public void WriteLine(string message) => Lines.Add(message);
    }
}
