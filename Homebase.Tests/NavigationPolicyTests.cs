using Xunit;

namespace Homebase.Tests;

public sealed class NavigationPolicyTests
{
    [Theory]
    [InlineData("YouTube", "https://www.youtube.com/watch?v=example", true)]
    [InlineData("YouTube", "https://accounts.google.com/signin", true)]
    [InlineData("YouTube", "https://consent.youtube.com/", true)]
    [InlineData("YouTube", "https://youtube.com.attacker.example/", false)]
    [InlineData("YouTube", "https://attacker-youtube.com/", false)]
    [InlineData("YouTube", "http://youtube.com/", false)]
    [InlineData("YouTube", "https://example.com/", false)]
    [InlineData("Netflix", "https://www.netflix.com/", true)]
    [InlineData("Netflix", "https://help.netflix.com/", true)]
    [InlineData("Netflix", "https://accounts.google.com/", false)]
    [InlineData("Spotify", "https://open.spotify.com/", true)]
    [InlineData("Spotify", "https://accounts.spotify.com/", true)]
    [InlineData("Spotify", "https://youtube.com/", false)]
    [InlineData("Unknown", "https://netflix.com/", false)]
    [InlineData("Netflix", "about:blank", true)]
    [InlineData("Spotify", "file:///C:/Windows/win.ini", false)]
    public void Navigation_is_limited_to_approved_https_hosts(
        string service, string uri, bool expected)
    {
        Assert.Equal(expected, NavigationPolicy.IsAllowedNavigation(service, uri));
    }

    [Theory]
    [InlineData("https://doubleclick.net/ad.js", true)]
    [InlineData("https://ads.example.doubleclick.net/ad.js", true)]
    [InlineData("https://googleadservices.com/pagead/conversion", true)]
    [InlineData("https://googlevideo.com/media", false)]
    [InlineData("https://not-doubleclick.net/ad.js", false)]
    [InlineData("file:///C:/tracking.a8.net", false)]
    [InlineData("not a URL", false)]
    public void Blocking_matches_only_configured_http_ad_hosts(string uri, bool expected)
    {
        Assert.Equal(expected, NavigationPolicy.IsBlockedAdHost(uri));
    }
}
