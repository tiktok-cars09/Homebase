using System.ComponentModel;
using System.Diagnostics;

namespace Homebase.Launcher;

public static class AppLauncher
{
    public static void Launch(LauncherEntry entry)
    {
        switch (entry.LaunchKind)
        {
            case LaunchKind.SteamUri:
                StartShellTarget(entry.LaunchTarget);
                break;
            case LaunchKind.MacApplication when OperatingSystem.IsMacOS():
                StartProcess("open", ["-a", entry.LaunchTarget]);
                break;
            case LaunchKind.LinuxDesktopEntry when OperatingSystem.IsLinux():
                LaunchLinuxDesktopEntry(entry);
                break;
            case LaunchKind.SystemShortcut when OperatingSystem.IsWindows():
                StartShellTarget(entry.LaunchTarget);
                break;
            default:
                throw new PlatformNotSupportedException(
                    $"'{entry.Name}' cannot be launched with the current operating system.");
        }
    }

    private static void LaunchLinuxDesktopEntry(LauncherEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.DesktopId))
        {
            throw new InvalidOperationException($"'{entry.Name}' has no Linux desktop entry ID.");
        }

        try
        {
            StartProcess("gtk-launch", [entry.DesktopId]);
        }
        catch (Win32Exception)
        {
            StartProcess("gio", ["launch", entry.LaunchTarget]);
        }
    }

    private static void StartShellTarget(string target)
    {
        var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        if (process is null)
        {
            throw new InvalidOperationException($"The operating system did not open '{target}'.");
        }
    }

    private static void StartProcess(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"The operating system could not start '{fileName}'.");
        }
    }
}
