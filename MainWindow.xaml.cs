using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Homebase.Launcher;
using Microsoft.Web.WebView2.Core;

namespace Homebase;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Homebase");
    private readonly List<TaskItem> _tasks = [];
    private readonly List<LauncherEntry> _apps = [];
    private readonly HashSet<string> _excludedAppIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _blockingEnabled = new(StringComparer.OrdinalIgnoreCase)
    {
        ["YouTube"] = true,
        ["Netflix"] = true,
        ["Spotify"] = true
    };
    private readonly HashSet<string> _initializingBrowsers = new(StringComparer.OrdinalIgnoreCase);
    private bool _focusMode;
    private bool _isClosing;
    private bool _appsScanned;

    public MainWindow()
    {
        InitializeComponent();
        TodayDate.Text = DateTime.Now.ToString("dddd, MMMM d");
        Greeting.Text = DateTime.Now.Hour switch
        {
            < 12 => "Good morning.",
            < 18 => "Good to see you.",
            _ => "Good evening."
        };
        LoadLocalData();
        RenderTasks();
        Loaded += InitializeBrowsersAsync;
        Closing += (_, _) => _isClosing = true;
        MainTabs.SelectionChanged += OnMainTabSelectionChanged;
        AddTaskButton.Click += (_, _) => AddTask();
        TaskInput.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                AddTask();
                args.Handled = true;
            }
        };
        QuickAddButton.Click += (_, _) => FocusTaskInput();
        ExploreButton.Click += (_, _) => FocusTaskInput();
        ClearCompletedButton.Click += (_, _) =>
        {
            _tasks.RemoveAll(task => task.Done);
            SaveLocalData();
            RenderTasks();
        };
        YouTubeHomeButton.Click += (_, _) => MainTabs.SelectedIndex = 1;
        NetflixHomeButton.Click += (_, _) => MainTabs.SelectedIndex = 2;
        SpotifyHomeButton.Click += (_, _) => MainTabs.SelectedIndex = 3;
        AppsHomeButton.Click += (_, _) => MainTabs.SelectedIndex = 4;
        ScanAppsButton.Click += (_, _) => ScanForApps();
        AppSearchInput.TextChanged += (_, _) => RenderApps();
        AppCategoryFilter.SelectionChanged += (_, _) => RenderApps();
        FocusButton.Click += (_, _) => ToggleFocusMode();
        ConfigureServiceControls();
        RenderApps();
    }

    private async void InitializeBrowsersAsync(object sender, RoutedEventArgs args)
    {
        try
        {
            await InitializeBrowserAsync("YouTube");
        }
        catch (Exception exception)
        {
            ShowBrowserError("YouTube", exception);
        }
    }

    private async void OnMainTabSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        var service = MainTabs.SelectedIndex switch
        {
            1 => "YouTube",
            2 => "Netflix",
            3 => "Spotify",
            _ => null
        };
        if (service is null)
        {
            if (MainTabs.SelectedIndex == 4 && !_appsScanned)
            {
                ScanForApps();
            }

            return;
        }

        try
        {
            await InitializeBrowserAsync(service);
        }
        catch (Exception exception)
        {
            ShowBrowserError(service, exception);
        }
    }

    private async Task InitializeBrowserAsync(string service)
    {
        var (browser, startUri) = GetService(service);
        if (browser.CoreWebView2 is not null || !_initializingBrowsers.Add(service))
        {
            return;
        }

        try
        {
            var profileDirectory = Path.Combine(_dataDirectory, "BrowserProfiles", service.ToLowerInvariant());
            Directory.CreateDirectory(profileDirectory);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profileDirectory);
            await browser.EnsureCoreWebView2Async(environment);

            if (_isClosing || browser.CoreWebView2 is null)
            {
                return;
            }

            var core = browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.NavigationStarting += (_, navigation) =>
            {
                if (!NavigationPolicy.IsAllowedNavigation(service, navigation.Uri))
                {
                    navigation.Cancel = true;
                    SetStatus($"Blocked navigation outside the approved {service} and sign-in sites.");
                }
            };
            core.NewWindowRequested += (_, request) =>
            {
                request.Handled = true;
                if (NavigationPolicy.IsAllowedNavigation(service, request.Uri))
                {
                    core.Navigate(request.Uri);
                }
                else
                {
                    SetStatus($"Blocked a new window outside the approved {service} and sign-in sites.");
                }
            };
            core.DownloadStarting += (_, download) => download.Cancel = true;
            core.PermissionRequested += (_, permission) =>
            {
                permission.State = CoreWebView2PermissionState.Deny;
                permission.Handled = true;
            };
            core.WebResourceRequested += (_, request) =>
            {
                if (IsBlockingEnabled(service) && NavigationPolicy.IsBlockedAdHost(request.Request.Uri))
                {
                    request.Response = core.Environment.CreateWebResourceResponse(
                        new MemoryStream(), 403, "Blocked by Homebase", "Content-Type: text/plain");
                }
            };
            core.ProcessFailed += (_, _) => SetStatus($"{service} stopped responding. Reload the tab to try again.");
            SetBlockingToggle(service);
            core.Navigate(startUri);
        }
        finally
        {
            _initializingBrowsers.Remove(service);
        }
    }

    private (Microsoft.Web.WebView2.Wpf.WebView2 Browser, string StartUri) GetService(string service) =>
        service switch
        {
            "YouTube" => (YouTubeView, "https://www.youtube.com/"),
            "Netflix" => (NetflixView, "https://www.netflix.com/"),
            "Spotify" => (SpotifyView, "https://open.spotify.com/"),
            _ => throw new ArgumentOutOfRangeException(nameof(service), service, "Unknown media service.")
        };

    private bool IsBlockingEnabled(string service) =>
        _blockingEnabled.TryGetValue(service, out var enabled) && enabled;

    private void ConfigureServiceControls()
    {
        var toggles = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase)
        {
            ["YouTube"] = YouTubeBlockToggle,
            ["Netflix"] = NetflixBlockToggle,
            ["Spotify"] = SpotifyBlockToggle
        };

        foreach (var (service, toggle) in toggles)
        {
            toggle.IsChecked = IsBlockingEnabled(service);
            toggle.Checked += (_, _) => SetBlocking(service, true);
            toggle.Unchecked += (_, _) => SetBlocking(service, false);
        }

        ConfigureBrowserButtons("YouTube", YouTubeBackButton, YouTubeForwardButton, YouTubeReloadButton);
        ConfigureBrowserButtons("Netflix", NetflixBackButton, NetflixForwardButton, NetflixReloadButton);
        ConfigureBrowserButtons("Spotify", SpotifyBackButton, SpotifyForwardButton, SpotifyReloadButton);
    }

    private void ConfigureBrowserButtons(string service, Button back, Button forward, Button reload)
    {
        back.Click += (_, _) =>
        {
            var browser = GetService(service).Browser;
            if (browser.CoreWebView2?.CanGoBack == true) browser.CoreWebView2.GoBack();
        };
        forward.Click += (_, _) =>
        {
            var browser = GetService(service).Browser;
            if (browser.CoreWebView2?.CanGoForward == true) browser.CoreWebView2.GoForward();
        };
        reload.Click += async (_, _) =>
        {
            var browser = GetService(service).Browser;
            if (browser.CoreWebView2 is not null) browser.CoreWebView2.Reload();
            else
            {
                try
                {
                    await InitializeBrowserAsync(service);
                }
                catch (Exception exception)
                {
                    ShowBrowserError(service, exception);
                }
            }
        };
    }

    private void SetBlockingToggle(string service)
    {
        var toggle = service switch
        {
            "YouTube" => YouTubeBlockToggle,
            "Netflix" => NetflixBlockToggle,
            "Spotify" => SpotifyBlockToggle,
            _ => throw new ArgumentOutOfRangeException(nameof(service))
        };
        toggle.IsChecked = IsBlockingEnabled(service);
    }

    private void SetBlocking(string service, bool enabled)
    {
        _blockingEnabled[service] = enabled;
        SaveLocalData();
        SetStatus($"{service} ad/tracker domain blocking {(enabled ? "on" : "off")}. Reload the tab to recheck the current page.");
    }

    private void LoadLocalData()
    {
        var path = Path.Combine(_dataDirectory, "home.json");
        if (!File.Exists(path))
        {
            _tasks.Add(new TaskItem("Add your first college deadline"));
            _tasks.Add(new TaskItem("Save a game or project to pick up later"));
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<SavedData>(File.ReadAllText(path));
            if (saved is null)
            {
                throw new JsonException("The saved data file is empty.");
            }

            _tasks.AddRange(saved.Tasks.Where(task => !string.IsNullOrWhiteSpace(task.Text)));
            foreach (var (service, enabled) in saved.BlockingEnabled)
            {
                if (_blockingEnabled.ContainsKey(service))
                {
                    _blockingEnabled[service] = enabled;
                }
            }
            _apps.AddRange((saved.Apps ?? [])
                .Where(app => app is not null &&
                              !string.IsNullOrWhiteSpace(app.Id) &&
                              !string.IsNullOrWhiteSpace(app.Name) &&
                              !string.IsNullOrWhiteSpace(app.LaunchTarget))
                .DistinctBy(app => app.Id, StringComparer.OrdinalIgnoreCase));
            _excludedAppIds.UnionWith(saved.ExcludedAppIds ?? []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _tasks.Clear();
            _tasks.Add(new TaskItem("Add your first college deadline"));
            _tasks.Add(new TaskItem("Save a game or project to pick up later"));
            SetStatus("Couldn't read your saved list. New changes will be saved here.");
            System.Diagnostics.Trace.TraceError($"Homebase could not load its saved data: {exception}");
        }
    }

    private void SaveLocalData()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            var data = new SavedData(_tasks, _blockingEnabled, _apps, _excludedAppIds.ToList());
            var json = JsonSerializer.Serialize(data, JsonOptions);
            var path = Path.Combine(_dataDirectory, "home.json");
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("Couldn't save changes on this device.");
            System.Diagnostics.Trace.TraceError($"Homebase could not save its data: {exception}");
        }
    }

    private void AddTask()
    {
        var text = TaskInput.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("Type something first, then add it to your list.");
            TaskInput.Focus();
            return;
        }

        _tasks.Insert(0, new TaskItem(text));
        TaskInput.Clear();
        SaveLocalData();
        RenderTasks();
        SetStatus("Added to your list.");
    }

    private void RenderTasks()
    {
        TaskList.Children.Clear();
        if (_tasks.Count == 0)
        {
            TaskList.Children.Add(new TextBlock
            {
                Text = "Nothing here yet. Add something whenever it comes to mind.",
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 11,
                Margin = new Thickness(3, 10, 3, 10)
            });
            return;
        }

        private void ScanForApps()
        {
            try
            {
                var result = AppDiscovery.Scan();
                var knownIds = new HashSet<string>(_apps.Select(app => app.Id), StringComparer.OrdinalIgnoreCase);
                var newApps = result.Entries
                    .Where(app => !_excludedAppIds.Contains(app.Id) && knownIds.Add(app.Id))
                    .ToArray();
                _apps.AddRange(newApps);
                _appsScanned = true;
                SaveLocalData();
                RenderApps();

                var status = $"Scan complete: {newApps.Length} new, {_apps.Count} in your library.";
                if (result.Warnings.Count > 0)
                {
                    status += $" {result.Warnings.Count} location(s) could not be read; see the app log for details.";
                    foreach (var warning in result.Warnings)
                    {
                        System.Diagnostics.Trace.TraceWarning($"Homebase app scan: {warning}");
                    }
                }

                AppStatusText.Text = status;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppStatusText.Text = $"Couldn't finish scanning: {exception.Message}";
                System.Diagnostics.Trace.TraceError($"Homebase app scan failed: {exception}");
            }
        }

        private void RenderApps()
        {
            if (AppList is null)
            {
                return;
            }

            AppList.Children.Clear();
            var query = AppSearchInput.Text?.Trim() ?? string.Empty;
            var category = (AppCategoryFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Everything";
            var visibleApps = _apps
                .Where(app =>
                    (category == "Everything" ||
                     app.Category.Equals(category == "Games" ? "Game" : "App", StringComparison.OrdinalIgnoreCase)) &&
                    (query.Length == 0 || app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (visibleApps.Length == 0)
            {
                AppList.Children.Add(new TextBlock
                {
                    Text = _apps.Count == 0
                        ? "No apps yet. Scan to find apps and Steam games installed on this PC."
                        : "No apps match this search.",
                    Foreground = (Brush)FindResource("MutedBrush"),
                    Margin = new Thickness(4, 12, 4, 12)
                });
                return;
            }

            foreach (var app in visibleApps)
            {
                AppList.Children.Add(CreateAppCard(app));
            }
        }

        private Border CreateAppCard(LauncherEntry app)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("PanelBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(new TextBlock { Text = app.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
            details.Children.Add(new TextBlock
            {
                Text = app.Category,
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0)
            });
            row.Children.Add(details);

            var launchButton = new Button
            {
                Content = "Launch",
                Style = (Style)FindResource("PrimaryButton"),
                Padding = new Thickness(14, 6),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            launchButton.Click += (_, _) => LaunchApp(app);
            Grid.SetColumn(launchButton, 1);
            row.Children.Add(launchButton);

            var removeButton = new Button
            {
                Content = "Remove",
                Padding = new Thickness(10, 6),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            removeButton.Click += (_, _) => RemoveApp(app);
            Grid.SetColumn(removeButton, 2);
            row.Children.Add(removeButton);
            card.Child = row;
            return card;
        }

        private void LaunchApp(LauncherEntry app)
        {
            try
            {
                AppLauncher.Launch(app);
                AppStatusText.Text = $"Launching {app.Name}...";
            }
            catch (Exception exception) when (
                exception is Win32Exception or InvalidOperationException or IOException or
                    UnauthorizedAccessException or PlatformNotSupportedException)
            {
                AppStatusText.Text = $"Couldn't launch {app.Name}: {exception.Message}";
                System.Diagnostics.Trace.TraceError($"Homebase could not launch '{app.Name}': {exception}");
            }
        }

        private void RemoveApp(LauncherEntry app)
        {
            _apps.RemoveAll(existing => existing.Id.Equals(app.Id, StringComparison.OrdinalIgnoreCase));
            _excludedAppIds.Add(app.Id);
            SaveLocalData();
            RenderApps();
            AppStatusText.Text = $"Removed {app.Name}. It won't be added again during scans.";
        }

        foreach (var task in _tasks)
        {
            var checkBox = new CheckBox
            {
                Content = task.Text,
                IsChecked = task.Done,
                Foreground = (Brush)FindResource("TextBrush"),
                FontSize = 12,
                Margin = new Thickness(3, 8, 3, 8),
                Tag = task
            };
            checkBox.Checked += TaskCompletionChanged;
            checkBox.Unchecked += TaskCompletionChanged;
            TaskList.Children.Add(checkBox);
            TaskList.Children.Add(new Border
            {
                Height = 1,
                Background = (Brush)FindResource("BorderBrush")
            });
        }
    }

    private void TaskCompletionChanged(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { Tag: TaskItem task } checkBox)
        {
            return;
        }

        task.Done = checkBox.IsChecked == true;
        SaveLocalData();
    }

    private void FocusTaskInput()
    {
        MainTabs.SelectedIndex = 0;
        TaskInput.Focus();
        TaskInput.BringIntoView();
    }

    private void ToggleFocusMode()
    {
        _focusMode = !_focusMode;
        YouTubeHomeButton.IsEnabled = !_focusMode;
        NetflixHomeButton.IsEnabled = !_focusMode;
        SpotifyHomeButton.IsEnabled = !_focusMode;
        YouTubeTab.IsEnabled = !_focusMode;
        NetflixTab.IsEnabled = !_focusMode;
        SpotifyTab.IsEnabled = !_focusMode;
        FocusButton.Content = _focusMode ? "Focus mode on" : "Focus mode";
        FocusButton.ToolTip = _focusMode
            ? "Focus mode is on. Turn it off to open media services."
            : "Focus mode disables the media shortcuts on Home.";
        SetStatus(_focusMode
            ? "Focus mode on. Your media shortcuts are paused."
            : "Focus mode off.");
    }

    private void ShowBrowserError(string service, Exception exception)
    {
        if (_isClosing)
        {
            return;
        }

        var message = exception.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase)
            ? "The Microsoft Edge WebView2 Runtime is missing. Install it from the download bundle, then reopen Homebase."
            : $"Couldn't start {service}: {exception.Message}";
        SetStatus(message);
        System.Diagnostics.Trace.TraceError($"Homebase could not initialize {service}: {exception}");
    }

    private void SetStatus(string message)
    {
        if (!_isClosing)
        {
            StatusText.Text = message;
        }
    }

    private sealed class TaskItem(string text)
    {
        public string Text { get; set; } = text;
        public bool Done { get; set; }
    }

    private sealed record SavedData(
        List<TaskItem> Tasks,
        Dictionary<string, bool> BlockingEnabled,
        List<LauncherEntry>? Apps = null,
        List<string>? ExcludedAppIds = null);
}
