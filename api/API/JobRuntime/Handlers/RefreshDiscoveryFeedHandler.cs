using API.Discovery;
using API.JobRuntime.Interfaces;
using API.Schema.DiscoveryContext;
using API.Schema.JobsContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>
/// Refreshes the Discover page's feed rails into <see cref="DiscoveryContext"/>. A subreddit whose
/// fetch fails (reddit 429s datacenter IPs freely) keeps its last good posts; the next hourly run
/// is the retry, so the rail degrades to stale instead of empty.
/// </summary>
public class RefreshDiscoveryFeedHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "RefreshDiscoveryFeed";
    public string JobType => Type;

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        await RefreshAsync(
            provider.GetRequiredService<DiscoveryContext>(),
            provider.GetRequiredService<IRedditFeedClient>(),
            provider.GetRequiredService<KenkuSettings>(),
            provider.GetRequiredService<IClock>(), ct);
    }

    public static async Task RefreshAsync(DiscoveryContext db, IRedditFeedClient reddit, KenkuSettings settings,
        IClock clock, CancellationToken ct)
    {
        foreach (string subreddit in settings.DiscoveryFeeds)
        {
            List<DiscoveryEntry> entries;
            try { entries = await reddit.GetHotAsync(subreddit, 10, ct); }
            catch (Exception) { continue; }

            db.Posts.RemoveRange(await db.Posts.Where(p => p.Rail == subreddit).ToListAsync(ct));
            db.Posts.AddRange(entries.Select((e, i) =>
                new DiscoveryPost(subreddit, i, e.Title, e.CoverUrl, e.Url, e.Source, e.Blurb, clock.UtcNow)));
        }
        await db.Sync(ct, typeof(RefreshDiscoveryFeedHandler), "Refresh discovery feed");
    }
}
