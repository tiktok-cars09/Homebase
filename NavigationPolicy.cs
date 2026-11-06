namespace Homebase;

internal static class NavigationPolicy
{
    private static readonly HashSet<string> BlockedHosts =
    [
        "doubleclick.net",
        "googleadservices.com",
        "googlesyndication.com",
        "adnxs.com",
        "adsrvr.org",
        "adform.net",
        "criteo.com",
        "outbrain.com",
        "taboola.com",
        "scorecardresearch.com",
        "tracking.a8.net"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> AllowedNavigationHosts =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["YouTube"] =
            [
                "youtube.com", "youtube-nocookie.com",
                "accounts.google.com", "accounts.youtube.com", "myaccount.google.com"
            ],
            ["Netflix"] = ["netflix.com"],
            ["Spotify"] = ["spotify.com"]
        };

    internal static bool IsAllowedNavigation(string service, string navigationUri)
    {
        if (!Uri.TryCreate(navigationUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsoluteUri.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !AllowedNavigationHosts.TryGetValue(service, out var allowedHosts))
        {
            return false;
        }

        return allowedHosts.Any(domain =>
            uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsBlockedAdHost(string requestUri)
    {
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) ||
            !(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return BlockedHosts.Any(domain =>
            uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase));
    }
}
