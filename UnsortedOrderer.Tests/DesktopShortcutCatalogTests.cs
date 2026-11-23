using UnsortedOrderer.Services.Desktop;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class DesktopShortcutCatalogTests
{
    [Fact]
    public void Every_desktop_category_has_at_least_100_distinct_executable_patterns()
    {
        Assert.True(DesktopShortcutCatalog.Games.Count >= 100);
        Assert.True(DesktopShortcutCatalog.PhotoEditors.Count >= 100);
        Assert.True(DesktopShortcutCatalog.Utilities.Count >= 100);
    }

    [Fact]
    public void No_executable_is_claimed_by_multiple_categories()
    {
        Assert.Empty(DesktopShortcutCatalog.Games.Intersect(DesktopShortcutCatalog.PhotoEditors,
            StringComparer.OrdinalIgnoreCase));
        Assert.Empty(DesktopShortcutCatalog.Games.Intersect(DesktopShortcutCatalog.Utilities,
            StringComparer.OrdinalIgnoreCase));
        Assert.Empty(DesktopShortcutCatalog.PhotoEditors.Intersect(DesktopShortcutCatalog.Utilities,
            StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Adobe\Adobe Lightroom Classic\Lightroom.exe", "PhotoEditors")]
    [InlineData(@"C:\Program Files\Adobe\Adobe Lightroom\Lightroom.exe", "PhotoEditors")]
    [InlineData(@"D:\Apps\Adobe\LightroomClassicCC.exe", "PhotoEditors")]
    [InlineData(@"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", "Games")]
    [InlineData(@"D:\Apps\EpicGamesLauncher.exe", "Games")]
    [InlineData(@"C:\Program Files\CPUID\CPU-Z\cpuz.exe", "Utilities")]
    [InlineData(@"D:\Apps\CPU-Z\cpuz64.exe", "Utilities")]
    [InlineData(@"D:\Apps\CPU-Z\cpuz_x64.exe", "Utilities")]
    [InlineData(@"D:\Apps\CPU-Z\cpu-z.exe", "Utilities")]
    [InlineData(@"C:\Games\Hades\hades.exe", "Games")]
    [InlineData(@"C:\Graphics\PhotoScape\photoscape.exe", "PhotoEditors")]
    [InlineData(@"C:\Tools\CrystalDiskInfo\crystaldiskinfo.exe", "Utilities")]
    public void Recognizes_reported_and_new_product_variants(string target, string expected)
    {
        Assert.Equal(expected, DesktopShortcutClassifier.GetFolder(new DesktopShortcut(target)));
    }

    [Theory]
    [InlineData("https://example.com/cpuz64.exe")]
    [InlineData("https://example.com/lightroom.exe")]
    [InlineData("https://example.com/epicgameslauncher.exe")]
    [InlineData(@"C:\Photos\lightroom.exe.txt")]
    [InlineData(@"C:\Apps\unrecognized.exe")]
    public void Does_not_reclassify_websites_documents_or_unknown_executables(string target)
    {
        Assert.Null(DesktopShortcutClassifier.GetFolder(new DesktopShortcut(target)));
    }
}
