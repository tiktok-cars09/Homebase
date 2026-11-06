using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Homebase.Launcher;

internal sealed class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly IBrush AccentBrush = Brush.Parse("#B5D96E");
    private static readonly IBrush MutedBrush = Brush.Parse("#A7A8AD");
    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Homebase",
        "Launcher");
    private readonly List<LauncherEntry> _entries = [];
    private readonly HashSet<string> _excludedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel _entryList = new() { Spacing = 8 };
    private readonly TextBox _searchBox = new() { Watermark = "Search apps and games..." };
    private readonly ComboBox _categoryBox = new()
    {
        ItemsSource = new[] { "Everything", "Apps", "Games" },
        SelectedIndex = 0,
        MinWidth = 120
    };
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = MutedBrush,
        FontSize = 12
    };

    public MainWindow()
    {
        Title = "Homebase Launcher";
        Width = 900;
        Height = 680;
        MinWidth = 560;
        MinHeight = 440;
        Background = Brush.Parse("#111214");
        Foreground = Brush.Parse("#F2F2F2");

        LoadLibrary();
        Content = CreateLayout();
        _searchBox.TextChanged += (_, _) => RenderEntries();
        _categoryBox.SelectionChanged += (_, _) => RenderEntries();
        Opened += (_, _) => ScanForApps();
        RenderEntries();
    }

    private Control CreateLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(24)
        };

        var header = new StackPanel { Spacing = 14 };
        var title = new TextBlock
        {
            Text = "Your games and apps",
            FontSize = 28,
            FontWeight = FontWeight.SemiBold
        };
        var subtitle = new TextBlock
        {
            Text = "A quick home for the apps already installed on this device.",
            Foreground = MutedBrush,
            FontSize = 13
        };
        var controls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8
        };
        controls.Children.Add(_searchBox);
        Grid.SetColumn(_categoryBox, 1);
        controls.Children.Add(_categoryBox);

        var scanButton = new Button
        {
            Content = "Scan for apps",
            Background = AccentBrush,
            Foreground = Brush.Parse("#202517"),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(14, 8)
        };
        scanButton.Click += (_, _) => ScanForApps();
        Grid.SetColumn(scanButton, 2);
        controls.Children.Add(scanButton);
        header.Children.Add(title);
        header.Children.Add(subtitle);
        header.Children.Add(controls);
        header.Children.Add(_status);
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            Content = _entryList,
            Margin = new Thickness(0, 20, 0, 0),
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        return root;
    }

    private void LoadLibrary()
    {
        var path = GetLibraryPath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var library = JsonSerializer.Deserialize<LauncherLibrary>(File.ReadAllText(path));
            if (library is null)
            {
                throw new JsonException("The saved launcher library is empty.");
            }

            _entries.AddRange((library.Entries ?? [])
                .Where(entry => entry is not null &&
                                !string.IsNullOrWhiteSpace(entry.Id) &&
                                !string.IsNullOrWhiteSpace(entry.Name) &&
                                !string.IsNullOrWhiteSpace(entry.LaunchTarget))
                .DistinctBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase));
            _excludedIds.UnionWith(library.ExcludedIds ?? []);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus("Couldn't read the saved launcher library. A new library will be created after scanning.");
            Trace.TraceError($"Homebase Launcher could not load its library: {exception}");
        }
    }

    private bool SaveLibrary()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            var json = JsonSerializer.Serialize(new LauncherLibrary(_entries, _excludedIds.ToList()), JsonOptions);
            var path = GetLibraryPath();
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("Couldn't save launcher changes on this device.");
            Trace.TraceError($"Homebase Launcher could not save its library: {exception}");
            return false;
        }
    }

    private void ScanForApps()
    {
        try
        {
            var result = AppDiscovery.Scan();
            var knownIds = new HashSet<string>(_entries.Select(entry => entry.Id), StringComparer.OrdinalIgnoreCase);
            var newEntries = result.Entries
                .Where(entry => !_excludedIds.Contains(entry.Id) && knownIds.Add(entry.Id))
                .ToArray();
            _entries.AddRange(newEntries);
            var saved = SaveLibrary();
            RenderEntries();

            if (saved)
            {
                var summary = $"Scan complete: {newEntries.Length} new, {_entries.Count} in your library.";
                if (result.Warnings.Count > 0)
                {
                    summary += $" {result.Warnings.Count} location(s) could not be read; see the app log for details.";
                }

                SetStatus(summary);
            }

            foreach (var warning in result.Warnings)
            {
                Trace.TraceWarning($"Homebase Launcher scan: {warning}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Couldn't finish scanning: {exception.Message}");
            Trace.TraceError($"Homebase Launcher scan failed: {exception}");
        }
    }

    private void RenderEntries()
    {
        _entryList.Children.Clear();
        var query = _searchBox.Text?.Trim() ?? string.Empty;
        var category = _categoryBox.SelectedItem as string ?? "Everything";
        var visibleEntries = _entries.Where(entry =>
                (category == "Everything" ||
                 entry.Category.Equals(category == "Games" ? "Game" : "App", StringComparison.OrdinalIgnoreCase)) &&
                (query.Length == 0 || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (visibleEntries.Length == 0)
        {
            _entryList.Children.Add(new TextBlock
            {
                Text = _entries.Count == 0
                    ? "No apps yet. Scan to find apps and Steam games installed on this device."
                    : "No apps match this search.",
                Foreground = MutedBrush,
                Margin = new Thickness(4, 12)
            });
            return;
        }

        foreach (var entry in visibleEntries)
        {
            _entryList.Children.Add(CreateEntryCard(entry));
        }
    }

    private Control CreateEntryCard(LauncherEntry entry)
    {
        var card = new Border
        {
            Background = Brush.Parse("#1B1C20"),
            BorderBrush = Brush.Parse("#303137"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14)
        };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10
        };
        var details = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        details.Children.Add(new TextBlock
        {
            Text = entry.Category,
            Foreground = MutedBrush,
            FontSize = 11
        });
        row.Children.Add(details);

        var launchButton = new Button
        {
            Content = "Launch",
            Background = AccentBrush,
            Foreground = Brush.Parse("#202517"),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(14, 7),
            VerticalAlignment = VerticalAlignment.Center
        };
        launchButton.Click += (_, _) => LaunchEntry(entry);
        Grid.SetColumn(launchButton, 1);
        row.Children.Add(launchButton);

        var removeButton = new Button
        {
            Content = "Remove",
            Padding = new Thickness(10, 7),
            VerticalAlignment = VerticalAlignment.Center
        };
        removeButton.Click += (_, _) => RemoveEntry(entry);
        Grid.SetColumn(removeButton, 2);
        row.Children.Add(removeButton);
        card.Child = row;
        return card;
    }

    private void LaunchEntry(LauncherEntry entry)
    {
        try
        {
            AppLauncher.Launch(entry);
            SetStatus($"Launching {entry.Name}...");
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException or
                UnauthorizedAccessException or PlatformNotSupportedException)
        {
            SetStatus($"Couldn't launch {entry.Name}: {exception.Message}");
            Trace.TraceError($"Homebase Launcher could not launch '{entry.Name}': {exception}");
        }
    }

    private void RemoveEntry(LauncherEntry entry)
    {
        _entries.RemoveAll(existing => existing.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
        _excludedIds.Add(entry.Id);
        var saved = SaveLibrary();
        RenderEntries();
        if (saved)
        {
            SetStatus($"Removed {entry.Name}. It won't be added again during scans.");
        }
    }

    private void SetStatus(string message) => _status.Text = message;

    private string GetLibraryPath() => Path.Combine(_dataDirectory, "library.json");

    private sealed record LauncherLibrary(
        List<LauncherEntry> Entries,
        List<string> ExcludedIds)
    {
        public LauncherLibrary() : this([], [])
        {
        }
    }
}
