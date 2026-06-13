namespace API.Services;

public static class HttpClientExtensions
{
    /// <summary>
    /// MangaDex rejects requests with an empty User-Agent (HTTP 400) and the Wikipedia/MediaWiki API
    /// requires a descriptive one (HTTP 403 otherwise), so every outbound metadata request must set it.
    /// </summary>
    public const string KenkuUserAgent = "Kenku/1.0 (+https://github.com/chutch3/kenku)";

    /// <summary>
    /// Sets the User-Agent as a raw header, replacing any existing one. Raw because the typed
    /// <c>DefaultRequestHeaders.UserAgent</c>/<c>Add</c> validate against product-token syntax and
    /// throw on otherwise-fine values (a user's custom agent, reddit's "platform:appid (comment)").
    /// </summary>
    public static HttpClient SetUserAgent(this HttpClient client, string userAgent)
    {
        client.DefaultRequestHeaders.Remove("User-Agent");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        return client;
    }

    /// <summary>Sets the Kenku User-Agent if one isn't already present, then returns the client.</summary>
    public static HttpClient WithKenkuUserAgent(this HttpClient client) =>
        client.DefaultRequestHeaders.UserAgent.Count == 0 ? client.SetUserAgent(KenkuUserAgent) : client;
}
