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

