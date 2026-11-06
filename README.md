# Homebase

Homebase is a Windows desktop home screen for everyday tasks and a few media
services. Its media area is a restricted WebView2 app, not a general-purpose
browser: there is no address bar, and page navigation is limited to YouTube,
Netflix, Spotify, and their required sign-in pages.
The separate Homebase Launcher is a cross-platform desktop app for finding and
opening installed apps and games.

## Download

Download `Homebase-win-x64.zip` from the
[GitHub Releases](../../releases) page, extract it, and run `Homebase.exe`.
The Windows zip also includes the new app and game launcher at
`Launcher\Homebase.Launcher.exe`.
The app is self-contained and does not require the .NET runtime to be installed.
The WebView2 runtime is a separate Microsoft component. It is included in the
release bundle as `MicrosoftEdgeWebView2Setup.exe`; install it if Homebase asks
for it. Windows 11 usually already has this runtime.

The original media home screen is currently a Windows x64 app. Its media pages
are provided by the services themselves: you need your own accounts, and each
service controls whether sign-in and playback work inside an embedded browser.
The separate launcher supports Windows, Linux, and macOS. On ChromeOS, run it
inside the Linux development environment; ChromeOS does not run this desktop app
natively.

## What works

- Separate YouTube, Netflix, and Spotify tabs, with no URL bar.
- Navigation and pop-up destinations checked against each service's approved
  host list. YouTube sign-in may use Google's account pages.
- Downloads, browser developer tools, context menus, and browser permission
  prompts are disabled in the service views.
- A local task list, focus-mode shortcut pause, and per-service ad/tracker
  blocking toggles.
- Task data and each service's browser profile are stored on this PC under
  `%LOCALAPPDATA%\Homebase`.

The blocker is intentionally described as a **basic domain blocker**. It blocks
a small built-in list of third-party ad and tracking domains; it is not uBlock
Origin, does not use uBlock's filter engine, and cannot reliably remove ads
served from the same domains as a service's content. Streaming services can
change their pages at any time. If a page stops working, turn blocking off for
that service and reload.

Service pages still load their own scripts, images, and media from third-party
content-delivery hosts. The navigation lock applies to page navigations and
pop-ups; it is not a network firewall. Service providers may change their
authentication, embedded-browser, DRM, or playback requirements without notice.

## Homebase Launcher

The launcher scans the standard application locations for each operating system:
Start Menu and desktop shortcuts on Windows, `.app` bundles on macOS, and
application menu entries (including Flatpak exports) on Linux. It also finds
installed Steam games in the usual Steam library locations on all three
platforms. It does not recursively crawl the whole disk. Scan results are kept
in the user's Homebase data folder; removed entries stay excluded from future
scans. Linux applications are opened through the desktop environment, and Steam
games are opened through the Steam protocol handler.

## Build from source

Requirements: Windows, the .NET 10 SDK, and an internet connection to restore the
WebView2 NuGet package.

```powershell
dotnet restore Homebase.csproj
dotnet test Homebase.Tests/Homebase.Tests.csproj -c Release
dotnet run --project Homebase.csproj
dotnet publish Homebase.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

The cross-platform launcher requires the .NET 10 SDK on Windows, Linux, or
macOS. ChromeOS users can build and run it inside the Linux development
environment.

```sh
dotnet test Homebase.Launcher.Tests/Homebase.Launcher.Tests.csproj -c Release
dotnet run --project Homebase.Launcher/Homebase.Launcher.csproj
```

Launcher release archives are built for `win-x64`, `linux-x64`, `linux-arm64`,
`osx-x64`, and `osx-arm64`. Each archive is self-contained and includes the
matching launcher executable and license. Linux still requires the standard
GUI libraries for the desktop environment.

The published Windows media app still needs the WebView2 Runtime. Microsoft's
Evergreen runtime bootstrapper is available from the
[WebView2 download page](https://developer.microsoft.com/microsoft-edge/webview2/).

## GitHub releases

Pushing a `v*` tag builds the Windows media app and the five self-contained
Homebase Launcher archives, then attaches them to a release. The Windows
`Homebase-win-x64.zip` contains both Windows apps. Standalone launcher archives
are named `Homebase-Launcher-<runtime>.zip`. For example:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## License

Homebase is licensed under the MIT License. See [LICENSE](LICENSE).
