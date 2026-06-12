using System.ComponentModel.DataAnnotations;

namespace API.Schema.DiscoveryContext;

/// <summary>
/// One cached feed post for the Discover page — written by the discovery feed refresh job, read by
/// the Feed endpoint, so a rate-limited fetch serves the rail's last good batch instead of nothing.
/// </summary>
public class DiscoveryPost : Identifiable
{
    /// <summary>Which rail the post belongs to (the subreddit name for reddit rails).</summary>
    [StringLength(128)] public string Rail { get; init; }

    /// <summary>Order within the rail's last successful fetch.</summary>
    public int Position { get; init; }

    public string Title { get; init; }
    public string CoverUrl { get; init; }
    public string Url { get; init; }
    [StringLength(128)] public string Source { get; init; }
    public string? Blurb { get; init; }
    public DateTime FetchedAt { get; init; }

    public DiscoveryPost(string rail, int position, string title, string coverUrl, string url, string source,
        string? blurb, DateTime fetchedAt)
    {
        Rail = rail;
        Position = position;
        Title = title;
        CoverUrl = coverUrl;
        Url = url;
        Source = source;
        Blurb = blurb;
        FetchedAt = fetchedAt;
    }
}
