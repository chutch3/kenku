namespace API.Discovery;

/// <summary>Owned seam for subreddit hot feeds (public .rss endpoints, no auth).</summary>
public interface IRedditFeedClient
{
    Task<List<DiscoveryEntry>> GetHotAsync(string subreddit, int limit, CancellationToken ct);
}
