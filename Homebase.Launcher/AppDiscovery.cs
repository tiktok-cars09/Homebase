using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Homebase.Launcher;

public sealed record DiscoveryResult(
    IReadOnlyList<LauncherEntry> Entries,
    IReadOnlyList<string> Warnings);

public static partial class AppDiscovery
{
    private static readonly Regex SteamAppIdPattern = new(
        "\"appid\"\\s+\"(?<id>\\d+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SteamNamePattern = new(
        "\"name\"\\s+\"(?<name>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SteamLibraryPathPattern = new(
        "\"path\"\\s+\"(?<path>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DiscoveryResult Scan()
    {
        var entries = new Dictionary<string, LauncherEntry>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            ScanWindowsShortcuts(entries, warnings);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ScanMacApplications(entries, warnings);
        }
        else if (OperatingSystem.IsLinux())
        {
            ScanLinuxDesktopEntries(entries, warnings);
        }

        ScanSteamGames(entries, warnings);
        return new DiscoveryResult(entries.Values.OrderBy(entry => entry.Name).ToArray(), warnings);
    }

    public static bool TryParseDesktopEntry(
        string contents,
        string desktopFilePath,
        string applicationRoot,
        out LauncherEntry? entry)
    {
        entry = null;
        var inDesktopEntry = false;
        string? name = null;
        string? executable = null;
        string? categories = null;
        string? type = null;
        var hidden = false;
        var noDisplay = false;

        foreach (var rawLine in contents.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inDesktopEntry = line.Equals("[Desktop Entry]", StringComparison.Ordinal);
                continue;
            }

            if (!inDesktopEntry || line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];
            switch (key)
            {
                case "Name":
                    name = UnescapeDesktopValue(value);
                    break;
                case "Exec":
                    executable = value;
                    break;
                case "Categories":
                    categories = value;
                    break;
                case "Type":
                    type = value;
                    break;
                case "Hidden":
                    hidden = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "NoDisplay":
                    noDisplay = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(executable) ||
            (type is not null && !type.Equals("Application", StringComparison.Ordinal)) ||
            hidden || noDisplay)
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(applicationRoot, desktopFilePath);
        var desktopId = relativePath
            .Replace(Path.DirectorySeparatorChar, '-')
            .Replace(Path.AltDirectorySeparatorChar, '-');
        var isGame = (categories ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Contains("Game", StringComparer.OrdinalIgnoreCase);

        entry = new LauncherEntry(
            $"desktop:{Path.GetFullPath(desktopFilePath)}",
            name,
            isGame ? "Game" : "App",
            Path.GetFullPath(desktopFilePath),
            LaunchKind.LinuxDesktopEntry,
            desktopId);
        return true;
    }

    public static bool TryParseSteamManifest(string contents, out string appId, out string name)
    {
        var appIdMatch = SteamAppIdPattern.Match(contents);
        var nameMatch = SteamNamePattern.Match(contents);
        if (!appIdMatch.Success || !nameMatch.Success)
        {
            appId = string.Empty;
            name = string.Empty;
            return false;
        }

        appId = appIdMatch.Groups["id"].Value;
        name = UnescapeVdfValue(nameMatch.Groups["name"].Value);
        return appId.Length > 0 && !string.IsNullOrWhiteSpace(name);
    }

    private static void ScanWindowsShortcuts(
        IDictionary<string, LauncherEntry> entries,
        ICollection<string> warnings)
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        foreach (var folder in folders.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct())
        {
            foreach (var shortcut in EnumerateFiles(folder, "*", SearchOption.AllDirectories, warnings)
                         .Where(path =>
                         {
                             var extension = Path.GetExtension(path);
                             return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                    extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
                         }))
            {
                var name = Path.GetFileNameWithoutExtension(shortcut);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AddEntry(entries, new LauncherEntry(
                        $"shortcut:{Path.GetFullPath(shortcut)}",
                        name,
                        "App",
                        Path.GetFullPath(shortcut),
                        LaunchKind.SystemShortcut));
                }
            }
        }
    }

    private static void ScanMacApplications(
        IDictionary<string, LauncherEntry> entries,
        ICollection<string> warnings)
    {
        var roots = new[]
        {
            "/Applications",
            "/System/Applications",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications")
        };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var application in EnumerateDirectories(root, "*.app", warnings))
            {
                var name = Path.GetFileNameWithoutExtension(application);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AddEntry(entries, new LauncherEntry(
                        $"mac:{Path.GetFullPath(application)}",
                        name,
                        "App",
                        Path.GetFullPath(application),
                        LaunchKind.MacApplication));
                }
            }
        }
    }

    private static void ScanLinuxDesktopEntries(
        IDictionary<string, LauncherEntry> entries,
        ICollection<string> warnings)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = Path.Combine(home, ".local", "share");
        }

        var dataDirectories = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        var sharedDataRoots = string.IsNullOrWhiteSpace(dataDirectories)
            ? new[] { "/usr/local/share", "/usr/share" }
            : dataDirectories.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var roots = new List<string>
        {
            Path.Combine(dataHome, "applications"),
            "/var/lib/flatpak/exports/share/applications",
            Path.Combine(home, ".local", "share", "flatpak", "exports", "share", "applications")
        };
        roots.AddRange(sharedDataRoots.Select(path => Path.Combine(path, "applications")));

        foreach (var root in roots.Distinct(StringComparer.Ordinal))
        {
            foreach (var desktopFile in EnumerateFiles(root, "*.desktop", SearchOption.AllDirectories, warnings))
            {
                try
                {
                    if (TryParseDesktopEntry(File.ReadAllText(desktopFile), desktopFile, root, out var entry) &&
                        entry is not null)
                    {
                        AddEntry(entries, entry);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Could not read desktop entry '{desktopFile}': {exception.Message}");
                }
            }
        }
    }

    private static void ScanSteamGames(
        IDictionary<string, LauncherEntry> entries,
        ICollection<string> warnings)
    {
        var roots = GetSteamRoots().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var steamAppsFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var steamApps = Path.Combine(root, "steamapps");
            if (Directory.Exists(steamApps))
            {
                steamAppsFolders.Add(steamApps);
            }

            var libraryFile = Path.Combine(steamApps, "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
            {
                continue;
            }

            try
            {
                var contents = File.ReadAllText(libraryFile);
                foreach (Match match in SteamLibraryPathPattern.Matches(contents))
                {
                    var libraryPath = UnescapeVdfValue(match.Groups["path"].Value);
                    var librarySteamApps = Path.Combine(libraryPath, "steamapps");
                    if (Directory.Exists(librarySteamApps))
                    {
                        steamAppsFolders.Add(librarySteamApps);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not read Steam library list '{libraryFile}': {exception.Message}");
            }
        }

        foreach (var steamApps in steamAppsFolders)
        {
            foreach (var manifest in EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly, warnings))
            {
                try
                {
                    if (!TryParseSteamManifest(File.ReadAllText(manifest), out var appId, out var name))
                    {
                        warnings.Add($"Steam manifest '{manifest}' did not contain a valid game name and ID.");
                        continue;
                    }

                    AddEntry(entries, new LauncherEntry(
                        $"steam:{appId}",
                        name,
                        "Game",
                        $"steam://rungameid/{appId}",
                        LaunchKind.SteamUri));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Could not read Steam manifest '{manifest}': {exception.Message}");
                }
            }
        }
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var roots = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Steam")
            };
            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (steamKey?.GetValue("SteamPath") is string registryPath &&
                !string.IsNullOrWhiteSpace(registryPath))
            {
                roots.Add(registryPath.Replace('/', Path.DirectorySeparatorChar));
            }

            return roots.Where(Directory.Exists);
        }

        if (OperatingSystem.IsMacOS())
        {
            var roots = new[]
            {
                Path.Combine(home, "Library", "Application Support", "Steam"),
                "/Applications/Steam.app/Contents/MacOS"
            };
            return roots.Where(Directory.Exists);
        }

        if (OperatingSystem.IsLinux())
        {
            var roots = new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                "/usr/share/steam"
            };
            return roots.Where(Directory.Exists);
        }

        return [];
    }

    private static IEnumerable<string> EnumerateFiles(
        string folder,
        string pattern,
        SearchOption searchOption,
        ICollection<string> warnings)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(folder, pattern, searchOption).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not scan '{folder}': {exception.Message}");
            return [];
        }
    }

    private static IEnumerable<string> EnumerateDirectories(
        string folder,
        string pattern,
        ICollection<string> warnings)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(folder, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not scan '{folder}': {exception.Message}");
            return [];
        }
    }

    private static void AddEntry(IDictionary<string, LauncherEntry> entries, LauncherEntry entry) =>
        entries.TryAdd(entry.Id, entry);

    private static string UnescapeDesktopValue(string value) =>
        value.Replace("\\s", " ", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static string UnescapeVdfValue(string value) =>
        value.Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal);
}
