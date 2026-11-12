using Newtonsoft.Json.Linq;
using PuppeteerSharp;

namespace API.HttpRequesters;

/// <summary>Opt-in beta deployment support for a solved Comix session file (userAgent + comix.to cookies).
/// The file is a runtime secret, not an image asset. This does not refresh or solve challenges.</summary>
internal static class ComixBrowserSessionBootstrap
{
    internal static (string UserAgent, CookieParam[] Cookies) Parse(string json)
    {
        JObject session = JObject.Parse(json);
        string? userAgent = session.Value<string>("userAgent");
        CookieParam[] cookies = session["cookies"]?.ToObject<CookieParam[]>() ?? [];
        if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Contains('\r') || userAgent.Contains('\n'))
            throw new InvalidOperationException("Comix browser session must include a valid user agent.");
        if (cookies.Length == 0 || cookies.Any(cookie =>
                string.IsNullOrEmpty(cookie.Domain)
                || !cookie.Domain.TrimStart('.').Equals("comix.to", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(cookie.Url)))
            throw new InvalidOperationException("Comix browser session must contain cookies scoped only to comix.to.");
        return (userAgent, cookies);
    }

    internal static async Task ImportFromFileAsync(ChromiumHeadlessBrowser browser, string path,
        CancellationToken cancellationToken = default)
    {
        (string userAgent, CookieParam[] cookies) = Parse(await File.ReadAllTextAsync(path, cancellationToken));
        await browser.ImportSessionAsync("Comix", userAgent, cookies, cancellationToken);
    }
}
