using UnsortedOrderer.Services.Desktop;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class ReportedDesktopShortcutTests
{
    // The screenshot shows display names, not TargetPath. These are representative link descriptors.
    [Theory]
    [InlineData("BSManager", @"C:\Apps\BSManager\BSManager.exe", "Games")]
    [InlineData("SteamVR", "steam://rungameid/250820", "VR")]
    [InlineData("SteamVR via executable", @"D:\SteamLibrary\steamapps\common\SteamVR\bin\win64\vrmonitor.exe", "VR")]
    [InlineData("Epic Games Launcher", @"C:\Program Files\Epic Games\Launcher\EpicGamesLauncher.exe", "Games")]
    [InlineData("Meta Horizon Link", @"C:\Program Files\Oculus\Support\oculus-client\OculusClient.exe", "VR")]
    [InlineData("Half-Life Alyx (Link)", "steam://rungameid/546560", "Games")]
    [InlineData("Half-Life Alyx via executable", @"D:\Games\Half-Life Alyx\game\bin\win64\hlvr.exe", "Games")]
    [InlineData("QLoader", @"C:\Apps\QLoader\QLoader.exe", "VR")]
    [InlineData("Epson Photo+ (first shortcut)", @"C:\Program Files\Epson Software\Epson Photo+\EPPlus.exe", "PhotoEditors")]
    [InlineData("Epson Photo+ (second shortcut)", @"C:\Program Files\Epson Software\Epson Photo+\Epson Photo+.exe", "PhotoEditors")]
    [InlineData("GIMP 3.0.8-1", @"C:\Program Files\GIMP 3\bin\gimp-3.0.exe", "PhotoEditors")]
    [InlineData("Inkscape", @"C:\Program Files\Inkscape\bin\inkscape.exe", "PhotoEditors")]
    [InlineData("Lightroom Classic", @"C:\Program Files\Adobe\Adobe Lightroom Classic\Lightroom.exe", "PhotoEditors")]
    [InlineData("Корзина", "shell:RecycleBinFolder", null)]
    [InlineData("Telegram", @"C:\Apps\Telegram\Telegram.exe", "Communication")]
    [InlineData("CrystalDisk", @"C:\Apps\CrystalDiskInfo\DiskInfo64.exe", "Utilities")]
    [InlineData("uTorrent Web", @"C:\Apps\uTorrent Web\utweb.exe", null)]
    [InlineData("Bambu Studio", @"C:\Program Files\Bambu Studio\bambu-studio.exe", "3DPrinting")]
    [InlineData("Docker Desktop", @"C:\Program Files\Docker\Docker\Docker Desktop.exe", "Development")]
    [InlineData("Visual Studio Code", @"C:\Apps\VS Code\Code.exe", "Development")]
    [InlineData("CPUID CPU-Z", @"C:\Program Files\CPUID\CPU-Z\cpuz.exe", "Utilities")]
    [InlineData("Яндекс Музыка", @"C:\Apps\Yandex Music\Яндекс Музыка.exe", "Music")]
    [InlineData("Яндекс.Диск", @"C:\Apps\YandexDisk\YandexDisk.exe", null)]
    [InlineData("GitHub Desktop", @"C:\Apps\GitHubDesktop\GitHubDesktop.exe", "Development")]
    [InlineData("Guitar Pro 8", @"C:\Apps\Guitar Pro 8\GuitarPro.exe", "Music")]
    [InlineData("GTP — концертные версии", @"D:\Guitar\GTP", null)]
    [InlineData("Raspberry Pi Imager", @"C:\Apps\Raspberry Pi Imager\rpi-imager.exe", "Utilities")]
    [InlineData("Яндекс Телемост", "https://telemost.yandex.ru", "Communication")]
    [InlineData("Moises", @"C:\Apps\Moises\Moises.exe", "Music")]
    [InlineData("_Unsorted — ярлык", @"D:\_Unsorted", null)]
    [InlineData("Prime Studio For M2", @"C:\Apps\Prime Studio For M2\Prime Studio For M2.exe", "Music")]
    [InlineData("AmneziaVPN", @"C:\Apps\AmneziaVPN\AmneziaVPN.exe", null)]
    [InlineData("rasphone — ярлык", @"C:\Windows\System32\rasphone.exe", null)]
    [InlineData("Autodesk Fusion", @"C:\Apps\Autodesk Fusion\Fusion360.exe", "3DPrinting")]
    [InlineData("repos — ярлык", @"D:\repos", null)]
    [InlineData("UnsortedOrderer", @"C:\Users\Andrey\Desktop\UnsortedOrderer.exe", null)]
    public void Screenshot_shortcut_fixtures(string label, string target, string? folder)
    {
        var actual = DesktopShortcutClassifier.GetFolder(new DesktopShortcut(target));
        Assert.True(folder == actual, $"{label}: expected {folder ?? "stay"}, got {actual ?? "stay"}");
    }

    [Theory]
    [InlineData("Яндекс Телемост", @"C:\Program Files\Google\Chrome\Application\chrome_proxy.exe", "--profile-directory=Default --app-id=abc123", "Communication")]
    [InlineData("Yandex Telemost", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge_proxy.exe", "--app-id=abc123", "Communication")]
    [InlineData("Яндекс Телемост", @"C:\Program Files\Yandex\YandexBrowser\Application\browser.exe", "--app=https://telemost.yandex.ru", "Communication")]
    [InlineData("Meta Horizon Link", @"C:\Program Files\Meta\Link\Launcher.exe", "", "VR")]
    [InlineData("Meta Horizon Link", @"shell:AppsFolder\Meta.Link_123!App", "", "VR")]
    [InlineData("Raspberry Pi Imager", @"C:\Program Files\Raspberry Pi Imager\App.exe", "", "Utilities")]
    [InlineData("Random Tool", @"C:\Apps\launcher.exe", "", null)]
    [InlineData("Яндекс Телемост", @"C:\Users\me\Documents\notes.txt", "", null)]
    [InlineData("Meta Horizon Link", @"D:\Pictures", "", null)]
    [InlineData("Raspberry Pi Imager", "", "", null)]
    public void Named_app_shortcuts_cover_wrapped_launchers_without_moving_files_or_folders(
        string label, string target, string arguments, string? expected)
    {
        Assert.Equal(expected, DesktopShortcutClassifier.GetFolder(new DesktopShortcut(target, arguments), label));
    }

    [Fact]
    public void Shell_link_with_no_readable_target_is_not_moved_by_guessing()
    {
        Assert.Null(DesktopShortcutClassifier.GetFolder(new DesktopShortcut("")));
    }
}
