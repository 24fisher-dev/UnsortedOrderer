namespace UnsortedOrderer.Services.Desktop;

public sealed record DesktopShortcut(string Target, string Arguments = "", string WorkingDirectory = "");

public static class DesktopShortcutClassifier
{
    public const string GamesFolder = "Games";
    public const string EditorsFolder = "PhotoEditors";
    public const string UtilitiesFolder = "Utilities";

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

        if (GameSchemes.Any(scheme => launchValue.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) ||
            IsStoreApp(launchValue, GameAppIds) || isExecutable && GameExecutables.Contains(executable))
        {
            return GamesFolder;
        }

        if (IsStoreApp(launchValue, EditorAppIds) || isExecutable &&
            (EditorExecutables.Contains(executable) && IsKnownEditorPath(executable, target) ||
             executable.StartsWith("gimp-", StringComparison.OrdinalIgnoreCase)))
        {
            return EditorsFolder;
        }

        if (isExecutable && UtilityExecutables.Contains(executable))
            return UtilitiesFolder;

        var workingDirectory = shortcut.WorkingDirectory.Replace('\\', '/').TrimEnd('/') + "/";
        return isExecutable &&
            GameDirectories.Any(directory => target.Contains(directory, StringComparison.OrdinalIgnoreCase) ||
                workingDirectory.Contains(directory, StringComparison.OrdinalIgnoreCase))
            ? GamesFolder : null;
    }

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
