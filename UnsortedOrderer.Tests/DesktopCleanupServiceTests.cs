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
            Assert.True(File.Exists(Path.Combine(desktop, "VR", "SteamVR.url")));
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
            Assert.False(File.Exists(publicDocument));
            Assert.Equal("preserve content", File.ReadAllText(Path.Combine(unsorted, "Public document.txt")));
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
            File.WriteAllText(Path.Combine(shared, "VR"), "not a directory");
            var log = new RecordingLog();
            var cleaner = new DesktopCleanupService(log, "", new ApplicationLocation(root, null), commonDesktopPath: shared);

            Assert.Equal(1, cleaner.CleanCommon(Path.Combine(root, "Unsorted")));
            Assert.True(File.Exists(steamLink));
            Assert.Contains(log.Lines, line => line.Contains(steamLink));
            Assert.Contains(log.Lines, line => line.Contains("объектов в папку разбора — 1"));
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

    [Fact]
    public void Desktop_cleanup_quarantines_empty_url_shortcut_but_preserves_script_and_valid_url()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
        var unsorted = Path.Combine(root, "Unsorted");
        var sorted = Path.Combine(root, "Sorted");
        try
        {
            var script = Path.Combine(desktop, "deploy.BAT");
            var broken = Path.Combine(desktop, "Empty.url");
            var valid = Path.Combine(desktop, "Web.url");
            File.WriteAllText(script, "@echo off");
            File.WriteAllText(broken, "[InternetShortcut]\nURL=");
            File.WriteAllText(valid, "[InternetShortcut]\nURL=https://example.com");
            var log = new RecordingLog();
            var cleaner = new DesktopCleanupService(log, desktop, new ApplicationLocation(root, null));

            Assert.Equal(0, cleaner.Clean(unsorted, sorted));

            Assert.True(File.Exists(script));
            Assert.True(File.Exists(valid));
            Assert.False(File.Exists(broken));
            Assert.Equal("[InternetShortcut]\nURL=", File.ReadAllText(Path.Combine(sorted, "_ToDelete", "Empty.url")));
            Assert.Contains(log.Lines, line => line.Contains("Битых ярлыков в корзине — 1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Windows_missing_local_shortcut_is_quarantined_and_valid_links_survive()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
        var existing = Directory.CreateDirectory(Path.Combine(root, "Exists")).FullName;
        var missing = Directory.CreateDirectory(Path.Combine(root, "Removed")).FullName;
        var sorted = Path.Combine(root, "Sorted");
        try
        {
            var store = new UnsortedOrderer.Services.Desktop.DesktopShortcutStore();
            var broken = Path.Combine(desktop, "Missing.lnk");
            var valid = Path.Combine(desktop, "Valid.lnk");
            store.CreateFolderLink(broken, missing);
            store.CreateFolderLink(valid, existing);
            Directory.Delete(missing);
            var cleaner = new DesktopCleanupService(new RecordingLog(), desktop, new ApplicationLocation(root, null), store);

            Assert.Equal(0, cleaner.Clean(Path.Combine(root, "Unsorted"), sorted));

            Assert.True(File.Exists(valid));
            Assert.False(File.Exists(broken));
            Assert.True(File.Exists(Path.Combine(sorted, "_ToDelete", "Missing.lnk")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Shortcut_is_not_quarantined_because_its_target_was_moved_in_the_same_run()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var desktop = Directory.CreateDirectory(Path.Combine(root, "Desktop")).FullName;
        var portable = Directory.CreateDirectory(Path.Combine(desktop, "Portable")).FullName;
        var unsorted = Path.Combine(root, "Unsorted");
        var sorted = Path.Combine(root, "Sorted");
        var link = Path.Combine(desktop, "Portable.lnk");
        try
        {
            File.WriteAllText(link, portable);
            var cleaner = new DesktopCleanupService(new RecordingLog(), desktop,
                new ApplicationLocation(root, null), new LocalFolderShortcutStore());

            Assert.Equal(0, cleaner.Clean(unsorted, sorted));

            Assert.True(Directory.Exists(Path.Combine(unsorted, "Portable")));
            Assert.True(File.Exists(link));
            Assert.False(Directory.Exists(Path.Combine(sorted, "_ToDelete")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class LocalFolderShortcutStore : UnsortedOrderer.Services.Desktop.IDesktopShortcutStore
    {
        public UnsortedOrderer.Services.Desktop.DesktopShortcut Read(string path) => new(File.ReadAllText(path));
        public bool IsBroken(string path, UnsortedOrderer.Services.Desktop.DesktopShortcut shortcut) =>
            !Directory.Exists(shortcut.Target);
        public void CreateFolderLink(string path, string targetDirectory) { }
    }

    [Fact]
    public void Checks_links_across_both_desktops_before_moving_files_and_keeps_same_named_files()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var personal = Directory.CreateDirectory(Path.Combine(root.FullName, "Personal")).FullName;
            var shared = Directory.CreateDirectory(Path.Combine(root.FullName, "Public")).FullName;
            var folder = Directory.CreateDirectory(Path.Combine(personal, "Portable")).FullName;
            var link = Path.Combine(shared, "Portable.lnk");
            File.WriteAllText(link, folder);
            File.WriteAllText(Path.Combine(personal, "same.conf"), "personal");
            File.WriteAllText(Path.Combine(shared, "same.conf"), "public");
            var unsorted = Path.Combine(root.FullName, "Unsorted");
            var sorted = Path.Combine(root.FullName, "Sorted");
            var cleaner = new DesktopCleanupService(new RecordingLog(), personal, new ApplicationLocation(root.FullName, null),
                new LocalFolderShortcutStore(), commonDesktopPath: shared);

            Assert.Equal(0, cleaner.CleanSelected(unsorted, sorted, personal: true, common: true));

            Assert.True(Directory.Exists(Path.Combine(unsorted, "Portable")));
            Assert.True(File.Exists(link));
            Assert.False(Directory.Exists(Path.Combine(sorted, "_ToDelete")));
            Assert.Equal("personal", File.ReadAllText(Path.Combine(unsorted, "same.conf")));
            Assert.Equal("public", File.ReadAllText(Path.Combine(unsorted, "same (1).conf")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Windows_shortcut_probe_preserves_virtual_targets_and_network_paths()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var link = Path.Combine(folder.FullName, "Link.lnk");
            var headerAndTerminal = new byte[0x50];
            headerAndTerminal[0] = 0x4c;
            new Guid("00021401-0000-0000-c000-000000000046").TryWriteBytes(headerAndTerminal.AsSpan(4, 16));
            File.WriteAllBytes(link, headerAndTerminal);
            var store = new UnsortedOrderer.Services.Desktop.DesktopShortcutStore();

            Assert.True(store.IsBroken(link, new("")));
            Assert.False(store.IsBroken(link, new(@"\\unavailable-server\share\Tool.exe")));
            // HasLinkTargetIDList can represent a valid shell item without a filesystem path.
            headerAndTerminal[20] = 1;
            File.WriteAllBytes(link, headerAndTerminal);
            Assert.False(store.IsBroken(link, new("")));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
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
