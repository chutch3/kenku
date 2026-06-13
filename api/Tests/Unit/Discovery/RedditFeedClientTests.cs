using API.Discovery;
using Xunit;

namespace API.Tests.Unit.Discovery;

/// <summary>Atom shape pinned against a captured live r/manga feed (2026-06-11).</summary>
public class RedditFeedClientTests
{
    [Fact]
    public void ConfigureClient_SetsTheUserAgent_WithoutTrippingTheStrictHeaderParser()
    {
        // Reddit's recommended UA ("kenku:discovery (self-hosted manga manager)") is rejected by the
        // typed UserAgent parser, which previously threw at client construction and wedged the feed
        // job in NeedsAttention. It must be set as a raw header instead.
        using var client = new HttpClient();

        RedditFeedClient.ConfigureClient(client);

        Assert.True(client.DefaultRequestHeaders.TryGetValues("User-Agent", out var values));
        Assert.Equal(RedditFeedClient.UserAgent, Assert.Single(values!));
    }

    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/">
          <title>/r/manga: manga, on reddit.</title>
          <entry>
            <author><name>/u/AutoModerator</name></author>
            <title>Weekly chapter discussion megathread</title>
            <link href="https://www.reddit.com/r/manga/comments/aaa/"/>
          </entry>
          <entry>
            <author><name>/u/reader</name></author>
            <title>Kagurabachi Chapter 100 just dropped</title>
            <link href="https://www.reddit.com/r/manga/comments/bbb/"/>
            <media:thumbnail url="https://b.thumbs.redditmedia.com/x.jpg"/>
          </entry>
        </feed>
        """;

    [Fact]
    public void ParseFeed_MapsEntries_AndDropsTheAutoModeratorPins()
    {
        List<DiscoveryEntry> entries = RedditFeedClient.ParseFeed(Feed, "manga");

        var entry = Assert.Single(entries);
        Assert.Equal("Kagurabachi Chapter 100 just dropped", entry.Title);
        Assert.Equal("https://www.reddit.com/r/manga/comments/bbb/", entry.Url);
        Assert.Equal("https://b.thumbs.redditmedia.com/x.jpg", entry.CoverUrl);
        Assert.Equal("r/manga", entry.Source);
    }
}
