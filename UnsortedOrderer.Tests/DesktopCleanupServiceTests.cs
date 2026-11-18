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
