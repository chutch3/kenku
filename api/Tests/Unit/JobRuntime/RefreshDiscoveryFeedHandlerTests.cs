using API.Discovery;
using API.JobRuntime.Handlers;
using API.Schema.DiscoveryContext;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace API.Tests.Unit.JobRuntime;

/// <summary>
/// The discovery feed refresh job: each configured subreddit's posts are cached in the database so
/// the Feed rail serves the last good batch even while reddit rate-limits the server.
/// </summary>
public class RefreshDiscoveryFeedHandlerTests
{
    private static readonly FakeClock Clock = new(new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc));

    private static DiscoveryContext NewContext() =>
        new(new DbContextOptionsBuilder<DiscoveryContext>()
            .UseInMemoryDatabase("discovery-" + Guid.NewGuid().ToString("N")).Options);

    private static DiscoveryEntry Entry(string title) => new(title, "c", $"https://reddit.test/{title}", "r/manga", null);

    [Fact]
    public async Task Refresh_CachesEachConfiguredSubreddit()
    {
        var ctx = NewContext();
        var settings = new KenkuSettings { DiscoveryFeeds = ["manga", "comicbooks"] };
        var reddit = new Mock<IRedditFeedClient>();
        reddit.Setup(r => r.GetHotAsync("manga", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry("Berserk thread")]);
        reddit.Setup(r => r.GetHotAsync("comicbooks", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry("Saga thread")]);

        await RefreshDiscoveryFeedHandler.RefreshAsync(ctx, reddit.Object, settings, Clock, default);

        var posts = await ctx.Posts.ToListAsync();
        Assert.Equal(2, posts.Count);
        Assert.Contains(posts, p => p.Rail == "manga" && p.Title == "Berserk thread");
        Assert.Contains(posts, p => p.Rail == "comicbooks" && p.Title == "Saga thread");
        Assert.All(posts, p => Assert.Equal(Clock.UtcNow, p.FetchedAt));
    }

    [Fact]
    public async Task Refresh_ReplacesARailsPreviousPosts()
    {
        var ctx = NewContext();
        var settings = new KenkuSettings { DiscoveryFeeds = ["manga"] };
        var reddit = new Mock<IRedditFeedClient>();
        reddit.SetupSequence(r => r.GetHotAsync("manga", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry("Old thread")])
            .ReturnsAsync([Entry("New thread"), Entry("Second thread")]);

        await RefreshDiscoveryFeedHandler.RefreshAsync(ctx, reddit.Object, settings, Clock, default);
        await RefreshDiscoveryFeedHandler.RefreshAsync(ctx, reddit.Object, settings, Clock, default);

        var posts = await ctx.Posts.OrderBy(p => p.Position).ToListAsync();
        Assert.Equal(["New thread", "Second thread"], posts.Select(p => p.Title));
    }

    [Fact]
    public async Task Refresh_KeepsStalePostsWhenAFetchFails()
    {
        var ctx = NewContext();
        var settings = new KenkuSettings { DiscoveryFeeds = ["manga"] };
        var reddit = new Mock<IRedditFeedClient>();
        reddit.SetupSequence(r => r.GetHotAsync("manga", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry("Last good thread")])
            .ThrowsAsync(new HttpRequestException("429"));

        await RefreshDiscoveryFeedHandler.RefreshAsync(ctx, reddit.Object, settings, Clock, default);
        await RefreshDiscoveryFeedHandler.RefreshAsync(ctx, reddit.Object, settings, Clock, default);

        Assert.Equal("Last good thread", Assert.Single(await ctx.Posts.ToListAsync()).Title);
    }
}
