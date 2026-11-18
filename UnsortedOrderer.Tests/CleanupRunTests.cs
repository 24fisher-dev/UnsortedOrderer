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

    private bool HasSortedFile(string name) =>
        Directory.EnumerateFiles(Destination, name, SearchOption.AllDirectories).Any();

    private ServiceProvider BuildRun(IDesktopCleanupService desktop, IDownloadsCleanupService downloads, Log log) =>
        new ServiceCollection()
            .AddSingleton<IMessageWriter>(log)
            .AddUnsortedOrdererApplication(new AppSettings(Source, Destination, "Soft", "Archives",
                "Images", "Photos", "Music", "Instruments", "EBooks", "Repositories", "Drivers", "Firmware",
                "Metadata", "Web", "Graphics", "Unknown", [], [], [], []))
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
