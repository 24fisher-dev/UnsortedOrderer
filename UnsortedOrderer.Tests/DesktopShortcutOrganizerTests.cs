using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;
using UnsortedOrderer.Services;
using UnsortedOrderer.Services.Desktop;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class DesktopShortcutOrganizerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", "", "", "Games")]
    [InlineData("steam://rungameid/730", "", "", "Games")]
    [InlineData("com.epicgames.launcher://apps/example?action=launch", "", "", "Games")]
    [InlineData(@"C:\Epic Games\Fortnite\launcher.exe", "", "", "Games")]
    [InlineData(@"C:\Program Files\Epic Games\Launcher\EpicGamesLauncher.exe", "", "", "Games")]
    [InlineData(@"C:\Windows\explorer.exe", @"shell:AppsFolder\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App", "", "Games")]
    [InlineData(@"C:\Apps\galaxyclient.exe", "", "", "Games")]
    [InlineData(@"C:\Apps\upc.exe", "", "", "Games")]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Game\game.exe", "", "", "Games")]
    [InlineData(@"D:\tool\game.exe", "", @"D:\XboxGames\Game\Content", "Games")]
    [InlineData(@"D:\Games\Title\Title.exe", "", "", "Games")]
    [InlineData(@"C:\Program Files\Adobe\Adobe Photoshop\Photoshop.exe", "", "", "PhotoEditors")]
    [InlineData(@"C:\Tools\Inkscape\inkscape.exe", "", "", "PhotoEditors")]
    [InlineData(@"C:\Tools\GIMP\gimp-3.0.exe", "", "", "PhotoEditors")]
    [InlineData(@"C:\Program Files\Affinity\Affinity Photo 2\Photo.exe", "", "", "PhotoEditors")]
    [InlineData(@"C:\Tools\AIDA64\aida64.exe", "", "", "Utilities")]
    [InlineData(@"C:\Program Files\MSI Afterburner\MSIAfterburner.exe", "", "", "Utilities")]
    [InlineData(@"C:\Tools\CPU-Z\cpuz_x64.exe", "", "", "Utilities")]
    [InlineData(@"C:\Tools\HWMonitor\HWMonitor.exe", "", "", "Utilities")]
    [InlineData(@"C:\Launchers\TLauncher.exe", "", "", "Games")]
    [InlineData(@"D:\Need for Speed\NFS11.exe", "", "", "Games")]
    [InlineData(@"C:\Tools\XnViewMP\xnviewmp.exe", "", "", "PhotoEditors")]
    [InlineData("https://example.com/cpuz.exe", "", "", null)]
    [InlineData(@"C:\Apps\designer.exe", "", "", null)]
    [InlineData(@"C:\Users\me\Pictures\Photoshop.psd", "", "", null)]
    [InlineData(@"C:\Program Files\Browser\browser.exe", "steam://rungameid/730", "", null)]
    [InlineData("https://example.com/steam", "", "", null)]
    [InlineData("https://example.com/games/steam.exe", "", "", null)]
    [InlineData("https://example.com/photoshop.exe", "", "", null)]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.GamingApp_package\XboxPcApp.exe", "", "", "Games")]
    [InlineData(@"C:\tools\unrecognized.exe", "", "", null)]
    public void Classifies_shortcut_by_target_path_or_launcher(string target, string arguments, string working, string? expected)
    {
        Assert.Equal(expected, DesktopShortcutClassifier.GetFolder(new DesktopShortcut(target, arguments, working)));
    }

    [Fact]
    public void Cleanup_groups_shortcuts_and_preserves_groups_and_folder_links_on_repeat()
    {
        var desktop = Directory.CreateDirectory(Path.Combine(_root, "Desktop")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(_root, "Unsorted")).FullName;
        var sorted = Directory.CreateDirectory(Path.Combine(desktop, "Output")).FullName;
        var exe = Path.Combine(desktop, "UnsortedOrderer.exe");
        File.WriteAllText(exe, "running");
        File.WriteAllText(Path.Combine(desktop, "plain.txt"), "move me");
        File.WriteAllText(Path.Combine(desktop, "Steam.url"), "[InternetShortcut]\nURL=steam://rungameid/123");
        File.WriteAllText(Path.Combine(desktop, "AIDA64.lnk"), @"C:\Apps\aida64.exe");
        File.WriteAllText(Path.Combine(desktop, "Krita.lnk"), @"C:\Apps\krita.exe");
        File.WriteAllText(Path.Combine(desktop, "Random.lnk"), @"C:\Apps\misc.exe");
        File.WriteAllText(Path.Combine(desktop, "Alias for output.lnk"), sorted);
        File.WriteAllText(Path.Combine(desktop, "Unsorted.lnk"), @"C:\Users\Other");
        File.WriteAllText(Path.Combine(sorted, "already sorted.txt"), "stay");
        var games = Directory.CreateDirectory(Path.Combine(desktop, "Games")).FullName;
        File.WriteAllText(Path.Combine(games, "Steam.url"), "old shortcut");

        var cleaner = new DesktopCleanupService(new NullLog(), desktop,
            new ApplicationLocation(desktop, exe), new TestStore());

        for (var run = 0; run < 2; run++)
        {
            cleaner.Clean(source, sorted);
            cleaner.CreateFolderShortcuts(source, sorted);
        }

        Assert.Equal("old shortcut", File.ReadAllText(Path.Combine(games, "Steam.url")));
        Assert.Equal(2, Directory.GetFiles(games, "*.url").Length);
        Assert.True(File.Exists(Path.Combine(desktop, "PhotoEditors", "Krita.lnk")));
        Assert.True(File.Exists(Path.Combine(desktop, "Utilities", "AIDA64.lnk")));
        Assert.True(File.Exists(Path.Combine(desktop, "Random.lnk")));
        Assert.True(File.Exists(Path.Combine(source, "plain.txt")));
        Assert.True(File.Exists(Path.Combine(sorted, "already sorted.txt")));
        Assert.Equal(source, File.ReadAllText(Path.Combine(desktop, "Unsorted (1).lnk")));
        Assert.Single(Directory.GetFiles(desktop, "Unsorted (*).lnk"));
        Assert.False(File.Exists(Path.Combine(desktop, "Sorted.lnk")));
        Assert.True(File.Exists(exe));
    }

    [Fact]
    public async Task Windows_shortcut_store_creates_and_reads_real_folder_link_on_background_thread()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory(_root);
        var link = Path.Combine(_root, "Unsorted.lnk");
        var target = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var store = new DesktopShortcutStore();

        await Task.Run(() => store.CreateFolderLink(link, target));
        var descriptor = await Task.Run(() => store.Read(link));

        Assert.True(File.Exists(link));
        Assert.Equal(target, descriptor?.Target, ignoreCase: true);
    }

    [Fact]
    public void Access_denied_on_shared_desktop_keeps_shortcut_and_explains_elevation()
    {
        var desktop = Directory.CreateDirectory(Path.Combine(_root, "Public Desktop")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(_root, "Unsorted")).FullName;
        var link = Path.Combine(desktop, "CPU-Z.lnk");
        File.WriteAllText(link, @"C:\Apps\CPU-Z\cpuz.exe");
        var log = new CapturingLog();
        var organizer = new DesktopShortcutOrganizer(new TestStore(), log,
            (_, _) => throw new UnauthorizedAccessException("Access denied"));

        var result = organizer.Group(desktop, source, null, [link]);

        Assert.Equal(0, result.Moved);
        Assert.Equal(1, result.Left);
        Assert.Equal(1, result.Errors);
        Assert.True(File.Exists(link));
        Assert.Contains(log.Messages, message => message.Contains("от имени администратора") &&
            message.Contains("оставлен на месте") && message.Contains("CPU-Z.lnk"));
    }

    private sealed class CapturingLog : IMessageWriter
    {
        public readonly List<string> Messages = [];
        public void WriteLine(string message) => Messages.Add(message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestStore : IDesktopShortcutStore
    {
        private readonly DesktopShortcutStore _real = new();

        public DesktopShortcut? Read(string path) => path.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
            ? _real.Read(path)
            : new DesktopShortcut(File.ReadAllText(path));

        public void CreateFolderLink(string path, string targetDirectory) =>
            File.WriteAllText(path, targetDirectory);
    }

    private sealed class NullLog : IMessageWriter
    {
        public void WriteLine(string message) { }
    }
}
