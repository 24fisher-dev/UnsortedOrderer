using UnsortedOrderer.Services.Desktop;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class ReportedDesktopShortcutTests
{
    // The screenshot shows display names, not TargetPath. These are representative link descriptors.
    [Theory]
    [InlineData("BSManager", @"C:\Apps\BSManager\BSManager.exe", "Games")]
    [InlineData("SteamVR", "steam://rungameid/250820", "Games")]
    [InlineData("SteamVR via executable", @"D:\SteamLibrary\steamapps\common\SteamVR\bin\win64\vrmonitor.exe", "Games")]
    [InlineData("Epic Games Launcher", @"C:\Program Files\Epic Games\Launcher\EpicGamesLauncher.exe", "Games")]
    [InlineData("Meta Horizon Link", @"C:\Program Files\Oculus\Support\oculus-client\OculusClient.exe", "Games")]
    [InlineData("Half-Life Alyx (Link)", "steam://rungameid/546560", "Games")]
    [InlineData("Half-Life Alyx via executable", @"D:\Games\Half-Life Alyx\game\bin\win64\hlvr.exe", "Games")]
    [InlineData("QLoader", @"C:\Apps\QLoader\QLoader.exe", "Games")]
    [InlineData("Epson Photo+ (first shortcut)", @"C:\Program Files\Epson Software\Epson Photo+\EPPlus.exe", "PhotoEditors")]
    [InlineData("Epson Photo+ (second shortcut)", @"C:\Program Files\Epson Software\Epson Photo+\Epson Photo+.exe", "PhotoEditors")]
    [InlineData("GIMP 3.0.8-1", @"C:\Program Files\GIMP 3\bin\gimp-3.0.exe", "PhotoEditors")]
    [InlineData("Inkscape", @"C:\Program Files\Inkscape\bin\inkscape.exe", "PhotoEditors")]
    [InlineData("Lightroom Classic", @"C:\Program Files\Adobe\Adobe Lightroom Classic\Lightroom.exe", "PhotoEditors")]
    [InlineData("Корзина", "shell:RecycleBinFolder", null)]
    [InlineData("Telegram", @"C:\Apps\Telegram\Telegram.exe", null)]
    [InlineData("CrystalDisk", @"C:\Apps\CrystalDiskInfo\DiskInfo64.exe", null)]
    [InlineData("uTorrent Web", @"C:\Apps\uTorrent Web\utweb.exe", null)]
    [InlineData("Bambu Studio", @"C:\Program Files\Bambu Studio\bambu-studio.exe", null)]
    [InlineData("Docker Desktop", @"C:\Program Files\Docker\Docker\Docker Desktop.exe", null)]
    [InlineData("Visual Studio Code", @"C:\Apps\VS Code\Code.exe", null)]
    [InlineData("CPUID CPU-Z", @"C:\Program Files\CPUID\CPU-Z\cpuz.exe", null)]
    [InlineData("Яндекс Музыка", @"C:\Apps\Yandex Music\Яндекс Музыка.exe", null)]
    [InlineData("Яндекс.Диск", @"C:\Apps\YandexDisk\YandexDisk.exe", null)]
    [InlineData("GitHub Desktop", @"C:\Apps\GitHubDesktop\GitHubDesktop.exe", null)]
    [InlineData("Guitar Pro 8", @"C:\Apps\Guitar Pro 8\GuitarPro.exe", null)]
    [InlineData("GTP — концертные версии", @"D:\Guitar\GTP", null)]
    [InlineData("Raspberry Pi Imager", @"C:\Apps\Raspberry Pi Imager\rpi-imager.exe", null)]
    [InlineData("Яндекс Телемост", "https://telemost.yandex.ru", null)]
    [InlineData("Moises", @"C:\Apps\Moises\Moises.exe", null)]
    [InlineData("_Unsorted — ярлык", @"D:\_Unsorted", null)]
    [InlineData("Prime Studio For M2", @"C:\Apps\Prime Studio For M2\Prime Studio For M2.exe", null)]
    [InlineData("AmneziaVPN", @"C:\Apps\AmneziaVPN\AmneziaVPN.exe", null)]
    [InlineData("rasphone — ярлык", @"C:\Windows\System32\rasphone.exe", null)]
    [InlineData("Autodesk Fusion", @"C:\Apps\Autodesk Fusion\Fusion360.exe", null)]
    [InlineData("repos — ярлык", @"D:\repos", null)]
    [InlineData("UnsortedOrderer", @"C:\Users\Andrey\Desktop\UnsortedOrderer.exe", null)]
    public void Screenshot_shortcut_fixtures(string label, string target, string? folder)
    {
        var actual = DesktopShortcutClassifier.GetFolder(new DesktopShortcut(target));
        Assert.True(folder == actual, $"{label}: expected {folder ?? "stay"}, got {actual ?? "stay"}");
    }

    [Fact]
    public void Shell_link_with_no_readable_target_is_not_moved_by_guessing()
    {
        Assert.Null(DesktopShortcutClassifier.GetFolder(new DesktopShortcut("")));
    }
}
