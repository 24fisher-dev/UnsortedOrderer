namespace UnsortedOrderer.Services.Desktop;

public sealed record DesktopShortcut(string Target, string Arguments = "", string WorkingDirectory = "");

public static class DesktopShortcutClassifier
{
    public const string GamesFolder = "Games";
    public const string EditorsFolder = "PhotoEditors";
    public const string UtilitiesFolder = "Utilities";
    public const string MusicFolder = "Music";
    public const string VrFolder = "VR";
    public const string DevelopmentFolder = "Development";
    public const string BrowsersFolder = "Browsers";
    public const string CommunicationFolder = "Communication";
    public const string PrintingFolder = "3DPrinting";

    private static readonly HashSet<string> UtilityExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "aida64.exe", "msiafterburner.exe", "rtss.exe", "cpuz.exe", "cpuz_x64.exe",
        "gpuz.exe", "gpu-z.exe", "hwinfo.exe", "hwinfo64.exe", "hwmonitor.exe", "hwmonitor_x64.exe",
        "diskinfo.exe", "diskinfo64.exe", "diskmark.exe", "diskmark64.exe", "rufus.exe",
        "autoruns.exe", "autoruns64.exe", "procexp.exe", "procexp64.exe", "procmon.exe", "procmon64.exe"
    };

    private static readonly HashSet<string> GameExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "steamwebhelper.exe", "epicgameslauncher.exe", "xbox.exe", "xboxpcapp.exe", "gamingapp.exe",
        "galaxyclient.exe", "battle.net.exe", "ubisoftconnect.exe", "upc.exe",
        "eadesktop.exe", "ealauncher.exe", "origin.exe", "riotclientservices.exe", "rockstargameslauncher.exe",
        "vrmonitor.exe", "steamvr.exe", "hlvr.exe", "bsmanager.exe", "oculusclient.exe", "metahorizonlink.exe",
        "qloader.exe", "minecraftlauncher.exe", "minecraft.exe", "robloxplayerbeta.exe", "robloxplayerlauncher.exe", "playnite.desktopapp.exe", "playnite.fullscreenapp.exe", "itch.exe", "tlauncher.exe", "nfshp.exe", "nfs11.exe"
    };

    private static readonly HashSet<string> EditorExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "photoshop.exe", "photoshopelementseditor.exe", "illustrator.exe", "lightroom.exe",
        "lightroomclassic.exe", "krita.exe", "gimp.exe", "inkscape.exe", "paintdotnet.exe", "mspaint.exe",
        "photo.exe", "designer.exe", "affinity.exe", "coreldrw.exe", "corelpp.exe",
        "psp.exe", "paintshoppro.exe", "captureone.exe", "darktable.exe", "rawtherapee.exe",
        "epplus.exe", "epsonphotoplus.exe", "epson photo+.exe", "photolab.exe", "aseprite.exe", "clipstudiopaint.exe", "figma.exe", "luminarneo.exe", "xnview.exe", "xnviewmp.exe"
    };

    private static readonly HashSet<string> VrExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "vrmonitor.exe", "steamvr.exe", "vrcompositor.exe", "vrserver.exe",
        "oculusclient.exe", "oculusdash.exe", "metahorizonlink.exe", "virtualdesktop.streamer.exe",
        "virtualdesktop.streamer", "pimaxplay.exe", "pimaxclient.exe", "viveconsole.exe",
        "sidequest.exe", "qloader.exe", "alvr_dashboard.exe"
    };
    private static readonly HashSet<string> MusicExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "spotify.exe", "foobar2000.exe", "aimp.exe", "musicbee.exe", "winamp.exe",
        "reaper.exe", "ableton live.exe", "fl64.exe", "fl.exe", "cubase.exe",
        "studio one.exe", "audacity.exe", "guitarpro.exe", "guitar pro 8.exe",
        "vlc.exe"
    };
    private static readonly HashSet<string> DevelopmentExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "code.exe", "codium.exe", "devenv.exe", "rider64.exe", "rider.exe",
        "idea64.exe", "pycharm64.exe", "webstorm64.exe", "datagrip64.exe",
        "postman.exe", "insomnia.exe", "gitkraken.exe", "sourcetree.exe",
        "githubdesktop.exe", "docker desktop.exe", "dbeaver.exe"
    };
    private static readonly HashSet<string> BrowserExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "firefox.exe", "msedge.exe", "brave.exe", "opera.exe",
        "vivaldi.exe", "browser.exe"
    };
    private static readonly HashSet<string> CommunicationExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "telegram.exe", "discord.exe", "slack.exe", "teams.exe", "zoom.exe",
        "signal.exe", "whatsapp.exe", "element.exe"
    };
    private static readonly HashSet<string> PrintingExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "bambu-studio.exe", "bambustudio.exe", "orca-slicer.exe", "orcaslicer.exe",
        "prusaslicer.exe", "cura.exe", "ultimakercura.exe", "lycheeslicer.exe",
        "chitubox.exe"
    };

    private static readonly string[] GameSchemes = [
        "steam://", "com.epicgames.launcher://", "uplay://", "ubisoftconnect://", "origin://",
        "origin2://", "eaapp://", "link2ea://", "goggalaxy://", "battlenet://", "ms-xbox:", "xbox:", "ms-gamingoverlay:"];
    private static readonly string[] GameDirectories = [
        "/steamapps/common/", "/xboxgames/", "/epic games/", "/gog games/", "/ea games/",
        "/ubisoft game launcher/games/", "/riot games/", "/games/", "/gog galaxy/games/"];
    private static readonly string[] GameAppIds = [
        "Microsoft.GamingApp_", "Microsoft.XboxApp_", "Microsoft.Minecraft", "Microsoft.XboxGamingOverlay_"];
    private static readonly string[] EditorAppIds = [
        "Microsoft.Paint_", "AffinityPhoto", "AffinityDesigner", "Adobe.Photoshop", "GIMP.GIMP", "Inkscape.Inkscape"];

    public static string? GetFolder(DesktopShortcut shortcut)
    {
        var target = shortcut.Target.Trim().Trim('"').Replace('\\', '/');
        var executable = target[(target.LastIndexOf('/') + 1)..];
        var arguments = shortcut.Arguments.Trim().Trim('"');
        var isExecutable = !target.Contains("://", StringComparison.Ordinal) &&
            executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        var launchValue = isExecutable && IsLauncher(executable) ? arguments : target;

        // VR launchers must not be swallowed by the broader Games category.
        if (isExecutable && VrExecutables.Contains(executable) ||
            launchValue.StartsWith("steam://run/250820", StringComparison.OrdinalIgnoreCase))
            return VrFolder;

        if (isExecutable && MusicExecutables.Contains(executable))
            return MusicFolder;
        if (isExecutable && DevelopmentExecutables.Contains(executable))
            return DevelopmentFolder;
        if (isExecutable && BrowsersExecutablesMatch(executable, target))
            return BrowsersFolder;
        if (isExecutable && CommunicationExecutables.Contains(executable))
            return CommunicationFolder;
        if (isExecutable && PrintingExecutables.Contains(executable))
            return PrintingFolder;

        if (GameSchemes.Any(scheme => launchValue.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) ||
            IsStoreApp(launchValue, GameAppIds) || isExecutable && (GameExecutables.Contains(executable) || DesktopShortcutCatalog.Games.Contains(executable)))
        {
            return GamesFolder;
        }

        if (IsStoreApp(launchValue, EditorAppIds) || isExecutable &&
            ((EditorExecutables.Contains(executable) || DesktopShortcutCatalog.PhotoEditors.Contains(executable)) && IsKnownEditorPath(executable, target) ||
             executable.StartsWith("gimp-", StringComparison.OrdinalIgnoreCase)))
        {
            return EditorsFolder;
        }

        if (isExecutable && (UtilityExecutables.Contains(executable) || DesktopShortcutCatalog.Utilities.Contains(executable)))
            return UtilitiesFolder;

        var workingDirectory = shortcut.WorkingDirectory.Replace('\\', '/').TrimEnd('/') + "/";
        return isExecutable &&
            GameDirectories.Any(directory => target.Contains(directory, StringComparison.OrdinalIgnoreCase) ||
                workingDirectory.Contains(directory, StringComparison.OrdinalIgnoreCase))
            ? GamesFolder : null;
    }

    private static bool BrowsersExecutablesMatch(string executable, string target) =>
        BrowserExecutables.Contains(executable) &&
        (!executable.Equals("browser.exe", StringComparison.OrdinalIgnoreCase) ||
            target.Contains("/Browser/", StringComparison.OrdinalIgnoreCase));

    private static bool IsKnownEditorPath(string executable, string target) =>
        !(executable.Equals("photo.exe", StringComparison.OrdinalIgnoreCase) ||
          executable.Equals("designer.exe", StringComparison.OrdinalIgnoreCase)) ||
        target.Contains("/Affinity/", StringComparison.OrdinalIgnoreCase) ||
        target.Contains("/Affinity ", StringComparison.OrdinalIgnoreCase);

    private static bool IsLauncher(string executable) =>
        executable.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) ||
        executable.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsStoreApp(string value, IEnumerable<string> identifiers)
    {
        const string prefix = "shell:AppsFolder/";
        value = value.Replace('\\', '/');
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            identifiers.Any(id => value.AsSpan(prefix.Length).StartsWith(id, StringComparison.OrdinalIgnoreCase));
    }
}
