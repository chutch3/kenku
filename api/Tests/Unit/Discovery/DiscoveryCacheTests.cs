using API.Discovery;
using API.Tests.Unit.JobRuntime;
using Xunit;

namespace API.Tests.Unit.Discovery;

public class DiscoveryCacheTests
{
    private static readonly DiscoveryEntry Entry = new("T", "c", "u", "s", null);

    [Fact]
    public async Task ServesTheCachedRail_WithinTheTtl_AndRefreshesAfter()
    {
        var clock = new FakeClock(new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc));
        var cache = new DiscoveryCache(clock);
        int calls = 0;

        for (int i = 0; i < 3; i++)
            await cache.GetOrRefreshAsync("rail", TimeSpan.FromHours(1), () => { calls++; return Task.FromResult(new List<DiscoveryEntry> { Entry }); });
        Assert.Equal(1, calls);

        clock.Advance(TimeSpan.FromMinutes(61));
        await cache.GetOrRefreshAsync("rail", TimeSpan.FromHours(1), () => { calls++; return Task.FromResult(new List<DiscoveryEntry> { Entry }); });
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ServesTheStaleRail_WhenARefreshFails()
    {
        var clock = new FakeClock(new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc));
        var cache = new DiscoveryCache(clock);

        await cache.GetOrRefreshAsync("rail", TimeSpan.FromHours(1), () => Task.FromResult(new List<DiscoveryEntry> { Entry }));
        clock.Advance(TimeSpan.FromHours(2));
        List<DiscoveryEntry> served = await cache.GetOrRefreshAsync("rail", TimeSpan.FromHours(1),
            () => throw new HttpRequestException("anilist down"));

        Assert.Single(served);
    }

    [Fact]
    public async Task ReturnsEmpty_WhenTheFirstFetchFails()
    {
        var cache = new DiscoveryCache(new FakeClock(DateTime.UtcNow));

        Assert.Empty(await cache.GetOrRefreshAsync("rail", TimeSpan.FromHours(1),
            () => throw new HttpRequestException("down")));
    }
}
