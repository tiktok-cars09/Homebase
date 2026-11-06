using Homebase.Launcher;
using Xunit;

namespace Homebase.Launcher.Tests;

public sealed class AppDiscoveryTests
{
    [Fact]
    public void Desktop_entry_uses_name_and_game_category()
    {
        const string contents = """
            [Desktop Entry]
            Type=Application
            Name=Example Game
            Exec=example-game
            Categories=Game;ActionGame;
            """;

        var found = AppDiscovery.TryParseDesktopEntry(
            contents,
            "/usr/share/applications/example.desktop",
            "/usr/share/applications",
            out var entry);

        Assert.True(found);
        Assert.NotNull(entry);
        Assert.Equal("Example Game", entry.Name);
        Assert.Equal("Game", entry.Category);
        Assert.Equal("example.desktop", entry.DesktopId);
    }

    [Theory]
    [InlineData("[Desktop Entry]\nType=Application\nName=Hidden\nHidden=true\n", false)]
    [InlineData("[Desktop Entry]\nType=Application\nName=Hidden\nNoDisplay=true\n", false)]
    [InlineData("[Desktop Entry]\nType=Link\nName=Website\n", false)]
    [InlineData("[Desktop Entry]\nType=Application\nExec=missing-name\n", false)]
    [InlineData("[Desktop Entry]\nType=Application\nName=Missing command\n", false)]
    public void Desktop_entry_skips_hidden_or_incomplete_items(string contents, bool expected)
    {
        Assert.Equal(expected, AppDiscovery.TryParseDesktopEntry(
            contents,
            "/applications/example.desktop",
            "/applications",
            out _));
    }

    [Fact]
    public void Steam_manifest_returns_installed_game_id_and_name()
    {
        const string manifest = """
            "AppState"
            {
                "appid" "570"
                "name" "Dota 2"
                "StateFlags" "4"
            }
            """;

        Assert.True(AppDiscovery.TryParseSteamManifest(manifest, out var appId, out var name));
        Assert.Equal("570", appId);
        Assert.Equal("Dota 2", name);
    }

    [Fact]
    public void Invalid_steam_manifest_is_rejected()
    {
        Assert.False(AppDiscovery.TryParseSteamManifest("\"AppState\" {}", out _, out _));
    }
}
