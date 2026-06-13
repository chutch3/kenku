using System.Net.Http.Json;
using API.Discovery;
using API.JobRuntime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// The feed pipeline through the real container: maintenance trigger → job store → dispatcher →
/// <see cref="API.JobRuntime.Handlers.RefreshDiscoveryFeedHandler"/> resolving its dependencies from
/// a real DI scope → DiscoveryContext → the Feed endpoint. The handler's scope resolution only
/// happens at job execution, so unit tests alone would let a registration regression reach
/// production as silently failing jobs.
/// </summary>
public class DiscoveryFeedEndToEndTests : IAsyncLifetime
{
    private sealed class FakeRedditFeedClient : IRedditFeedClient
    {
        public Task<List<DiscoveryEntry>> GetHotAsync(string subreddit, int limit, CancellationToken ct) =>
            Task.FromResult(new List<DiscoveryEntry>
            {
                new($"{subreddit} hot thread", "c", $"https://reddit.test/{subreddit}", $"r/{subreddit}", null),
            });
    }

    private readonly PostgresFixture _postgres = new();
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        _app = new KenkuApplicationFactory
        {
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
            ExtraServices = services => services.AddSingleton<IRedditFeedClient>(new FakeRedditFeedClient()),
        };
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        await _postgres.DropDatabaseAsync(_dbName);
    }

    [Fact]
    public async Task FeedRefresh_RunsThroughTheRealContainer_AndTheFeedEndpointServesIt()
    {
        using HttpClient http = _app.CreateClient();

        // The same path the Maintenance button takes.
        (await http.PostAsync("/v2/Maintenance/RefreshDiscoveryFeeds", null)).EnsureSuccessStatusCode();

        using (var scope = _app.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<Dispatcher>();
            while (await dispatcher.RunOnceAsync()) { }
        }

        var feed = await http.GetFromJsonAsync<List<DiscoveryEntry>>("/v2/Discover/Feed");
        Assert.NotNull(feed);
        Assert.Contains(feed!, e => e.Source == "r/manga" && e.Title == "manga hot thread");
        Assert.Contains(feed!, e => e.Source == "r/comicbooks");
    }
}
