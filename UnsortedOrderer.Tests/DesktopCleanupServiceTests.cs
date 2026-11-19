using System;
using System.IO;
using System.Reflection;
using UnsortedOrderer.Services;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;
using Xunit;

namespace UnsortedOrderer.Tests;

public class DesktopCleanupServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Desktop_cleanup_preserves_running_app_but_imports_old_console_build(bool nested)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var desktop = Path.Combine(root, "Desktop");
        var appDirectory = nested ? Path.Combine(desktop, "portable", "current") : desktop;
        var destination = Path.Combine(root, "unsorted");
        Directory.CreateDirectory(appDirectory);
        try
        {
            var exe = Path.Combine(appDirectory, "Renamed release.exe");
            var settings = Path.Combine(appDirectory, "appsettings.json");
            var olderBuild = Path.Combine(desktop, "UnsortedOrderer.Console.exe");
            var similarlyNamed = Path.Combine(desktop, "Renamed release old.exe");
            File.WriteAllText(exe, "current");
            File.WriteAllText(settings, "{}");
            File.WriteAllText(olderBuild, "old");
            File.WriteAllText(similarlyNamed, "unrelated");
            var cleaner = new DesktopCleanupService(new NullLog(), desktop, new ApplicationLocation(appDirectory, exe));

            cleaner.Clean(destination);

            Assert.Equal("current", File.ReadAllText(exe));
            Assert.Equal("{}", File.ReadAllText(settings));
            Assert.Equal("old", File.ReadAllText(Path.Combine(destination, Path.GetFileName(olderBuild))));
            Assert.Equal("unrelated", File.ReadAllText(Path.Combine(destination, Path.GetFileName(similarlyNamed))));
            Assert.False(File.Exists(olderBuild));
            Assert.False(File.Exists(similarlyNamed));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Desktop_cleanup_keeps_working_and_groups_shortcuts_when_one_entry_fails(bool accessDenied)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
        var destination = Path.Combine(root, "Unsorted");
        var lockedFolder = Directory.CreateDirectory(Path.Combine(desktop, "locked")).FullName;
        var logger = new RecordingLog();
        try
        {
            File.WriteAllText(Path.Combine(lockedFolder, "data.txt"), "preserve");
            File.WriteAllText(Path.Combine(desktop, "transfer.txt"), "move");
            File.WriteAllText(Path.Combine(desktop, "SteamVR.url"), "[InternetShortcut]\nURL=steam://rungameid/250820");
            var cleaner = new DesktopCleanupService(logger, desktop, new ApplicationLocation(desktop, null),
                moveEntry: (entry, target) =>
                {
                    if (entry.FullName == lockedFolder)
                        throw accessDenied ? new UnauthorizedAccessException("locked") : new IOException("locked");
                    File.Move(entry.FullName, target);
                });

            cleaner.Clean(destination);

            Assert.Equal("preserve", File.ReadAllText(Path.Combine(lockedFolder, "data.txt")));
            Assert.Equal("move", File.ReadAllText(Path.Combine(destination, "transfer.txt")));
            Assert.True(File.Exists(Path.Combine(desktop, "Games", "SteamVR.url")));
            Assert.Contains(logger.Lines, line => line.Contains(lockedFolder) && line.Contains("locked"));
            Assert.Contains(logger.Lines, line => line.Contains("Ошибок переноса: 1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Desktop_cleanup_is_available_when_app_runs_from_downloads()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var desktop = Directory.CreateDirectory(Path.Combine(root, "Personal")).FullName;
        var downloads = Directory.CreateDirectory(Path.Combine(root, "Downloads")).FullName;
        var unsorted = Path.Combine(root, "Unsorted");
        var document = Path.Combine(desktop, "notes.txt");
        var log = new RecordingLog();
        try
        {
            File.WriteAllText(document, "desktop notes");
            var cleaner = new DesktopCleanupService(log, desktop, new ApplicationLocation(downloads, null));

            Assert.True(cleaner.CanClean(unsorted));
            cleaner.Clean(unsorted);

            Assert.False(File.Exists(document));
            Assert.Equal("desktop notes", File.ReadAllText(Path.Combine(unsorted, "notes.txt")));
            Assert.Contains(log.Lines, line => line.Contains(desktop));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Personal_and_shared_desktops_are_processed_separately()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var personal = Directory.CreateDirectory(Path.Combine(root, "Personal")).FullName;
        var shared = Directory.CreateDirectory(Path.Combine(root, "Shared")).FullName;
        var unsorted = Path.Combine(root, "Unsorted");
        var log = new RecordingLog();
        try
        {
            var personalLink = Path.Combine(personal, "CPUID CPU-Z.lnk");
            var sharedLink = Path.Combine(shared, "CPUID HWMonitor.lnk");
            var commonGame = Path.Combine(shared, "Need for Speed.lnk");
            var publicDocument = Path.Combine(shared, "Public document.txt");
            var personalSystemFile = Path.Combine(personal, "desktop.ini");
            foreach (var path in new[] { personalLink, sharedLink, commonGame, publicDocument, personalSystemFile })
                File.WriteAllText(path, "preserve content");
            var cleaner = new DesktopCleanupService(log, personal, new ApplicationLocation(root, null),
                new TestShortcutStore(new Dictionary<string, string>
                {
                    [personalLink] = @"C:\Program Files\CPUID\CPU-Z\cpuz_x64.exe",
                    [sharedLink] = @"C:\Program Files\CPUID\HWMonitor\HWMonitor.exe",
                    [commonGame] = @"D:\Need for Speed\NFSHP.exe"
                }), commonDesktopPath: shared);

            Assert.Equal(0, cleaner.Clean(unsorted));
            Assert.True(File.Exists(Path.Combine(personal, "Utilities", "CPUID CPU-Z.lnk")));
            Assert.True(File.Exists(sharedLink));
            Assert.True(File.Exists(personalSystemFile));

            Assert.Equal(0, cleaner.CleanCommon(unsorted));
            Assert.True(File.Exists(Path.Combine(shared, "Utilities", "CPUID HWMonitor.lnk")));
            Assert.True(File.Exists(Path.Combine(shared, "Games", "Need for Speed.lnk")));
            Assert.True(File.Exists(publicDocument));
            Assert.Contains(log.Lines, line => line.Contains(shared));
            Assert.Contains(log.Lines, line => line.Contains("Ярлыков сгруппировано — 2"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_blocked_shortcut_group_preserves_the_link_and_reports_failure()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var shared = Directory.CreateDirectory(Path.Combine(root, "Shared")).FullName;
        try
        {
            var steamLink = Path.Combine(shared, "Steam.url");
            File.WriteAllText(steamLink, "[InternetShortcut]\nURL=steam://rungameid/250820");
            File.WriteAllText(Path.Combine(shared, "Games"), "not a directory");
            var log = new RecordingLog();
            var cleaner = new DesktopCleanupService(log, "", new ApplicationLocation(root, null), commonDesktopPath: shared);

            Assert.Equal(1, cleaner.CleanCommon(Path.Combine(root, "Unsorted")));
            Assert.True(File.Exists(steamLink));
            Assert.Contains(log.Lines, line => line.Contains(steamLink));
            Assert.Contains(log.Lines, line => line.Contains("ничего не перемещено"));
            Assert.Contains(log.Lines, line => line.Contains("Ошибок переноса: 1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestShortcutStore(IReadOnlyDictionary<string, string> targets) : UnsortedOrderer.Services.Desktop.IDesktopShortcutStore
    {
        public UnsortedOrderer.Services.Desktop.DesktopShortcut? Read(string path) =>
            targets.TryGetValue(path, out var target) ? new(target) : null;
        public void CreateFolderLink(string path, string targetDirectory) { }
    }

    private sealed class RecordingLog : IMessageWriter
    {
        public List<string> Lines { get; } = [];
        public void WriteLine(string message) => Lines.Add(message);
    }

    private sealed class NullLog : IMessageWriter
    {
        public void WriteLine(string message) { }
    }

    [Theory]
    [InlineData(".lnk")]
    [InlineData(".url")]
    public void IsShortcut_ReturnsTrue_ForKnownExtensions(string extension)
    {
        var isShortcutMethod = typeof(DesktopCleanupService)
            .GetMethod("IsShortcut", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("IsShortcut method not found.");

        var filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + extension);
        File.WriteAllText(filePath, string.Empty);

        try
        {
            var result = (bool)isShortcutMethod.Invoke(null, new object[] { new FileInfo(filePath) })!;

            Assert.True(result);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
