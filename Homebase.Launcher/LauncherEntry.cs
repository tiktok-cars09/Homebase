namespace Homebase.Launcher;

public enum LaunchKind
{
    SystemShortcut,
    SteamUri,
    MacApplication,
    LinuxDesktopEntry
}

public sealed record LauncherEntry(
    string Id,
    string Name,
    string Category,
    string LaunchTarget,
    LaunchKind LaunchKind,
    string? DesktopId = null);
