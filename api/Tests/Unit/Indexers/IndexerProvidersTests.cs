using System.Net;
using API;
using API.Indexers;
using Xunit;

namespace API.Tests.Unit.Indexers;

public class IndexerProvidersTests
{
    private static HttpClient FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new FakeHttpMessageHandler(handler));

    // ---------- ConfiguredIndexerProvider (manual) ----------

    [Fact]
    public async Task ConfiguredProvider_YieldsOneIndexerPerManualEntry()
    {
        var http = FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var provider = new ConfiguredIndexerProvider(http,
        [
            new ManualIndexerConfig("Tracker A", "http://a.test/api", "ka", [8000]),
            new ManualIndexerConfig("Tracker B", "http://b.test/api", "kb", [8000, 8020])
        ]);

        var indexers = await provider.GetIndexersAsync(CancellationToken.None);

        Assert.Equal(2, indexers.Count);
        Assert.Equal(new[] { "Tracker A", "Tracker B" }, indexers.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task ConfiguredProvider_EmptyWhenNoManualIndexers()
    {
        var http = FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var provider = new ConfiguredIndexerProvider(http, []);

        Assert.Empty(await provider.GetIndexersAsync(CancellationToken.None));
    }

    // ---------- SyncedIndexerProvider (Prowlarr push) ----------

    [Fact]
    public async Task SyncedProvider_YieldsOneTorznabPerEnabledIndexer()
    {
        var http = FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var settings = new KenkuSettings { AppData = NewTmp() };
        settings.SyncedIndexers.Add(new SyncedIndexerConfig(1, "Nyaa", "http://p/1/api", "k", [7030], "torrent", true));
        settings.SyncedIndexers.Add(new SyncedIndexerConfig(2, "Off", "http://p/2/api", "k", [7030], "torrent", false));

        var provider = new SyncedIndexerProvider(http, settings);
        var indexers = await provider.GetIndexersAsync(CancellationToken.None);

        Assert.Single(indexers);
        Assert.Equal("Nyaa", indexers[0].Name);
    }

    [Fact]
    public async Task SyncedProvider_ReflectsLiveChangesBetweenCalls()
    {
        var http = FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var settings = new KenkuSettings { AppData = NewTmp() };
        var provider = new SyncedIndexerProvider(http, settings);

        Assert.Empty(await provider.GetIndexersAsync(CancellationToken.None));

        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Nyaa", "http://p/1/api", "k", [7030], "torrent", true));

        var after = await provider.GetIndexersAsync(CancellationToken.None);
        Assert.Single(after);
        Assert.Equal("Nyaa", after[0].Name);
    }

    [Fact]
    public async Task SyncedProvider_EmptyWhenNoIndexers()
    {
        var http = FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var settings = new KenkuSettings { AppData = NewTmp() };
        var provider = new SyncedIndexerProvider(http, settings);

        Assert.Empty(await provider.GetIndexersAsync(CancellationToken.None));
    }

    private static string NewTmp() => Path.Combine(Path.GetTempPath(), $"kenku-test-{Guid.NewGuid():N}");
}
