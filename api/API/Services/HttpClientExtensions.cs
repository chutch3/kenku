namespace API.Services;

public static class HttpClientExtensions
{
    /// <summary>
    /// MangaDex rejects requests with an empty User-Agent (HTTP 400) and the Wikipedia/MediaWiki API
    /// requires a descriptive one (HTTP 403 otherwise), so every outbound metadata request must set it.
    /// </summary>
    public const string KenkuUserAgent = "Kenku/1.0 (+https://github.com/chutch3/kenku)";

    /// <summary>Sets the Kenku User-Agent if one isn't already present, then returns the client.</summary>
    public static HttpClient WithKenkuUserAgent(this HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            client.DefaultRequestHeaders.UserAgent.ParseAdd(KenkuUserAgent);
        return client;
    }
}
