using API.Services.Interfaces;
using API.JobRuntime.Interfaces;
using API.JobRuntime.Reconcilers;
using System.Net;
using System.Text.Json;
using API;
using API.Indexers;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// True end-to-end coverage of the indexer search path: the real application is hosted in-process,
/// the DI container builds the whole search stack (SearchController → IndexerBackedSeriesSource →
/// AggregateIndexerSearch → TorznabIndexer), and the only edge swapped is the indexer endpoint, which
/// points at a WireMock server. Search is driven through the real <c>/v2/Search</c> HTTP endpoint.
///
/// The headline case is a regression guard: a synced indexer carries its own (Prowlarr-mapped) comic
/// categories, and the search must send THOSE to the indexer — not the global IndexerComicCategories
/// fallback. Sending the global guess instead silently filters every comic out on trackers that tag
/// comics under a different category id, which is exactly how "search The Boys returns nothing" looked.
/// </summary>
[Trait("Category", "Integration")]
public class IndexerSearchEndToEndTests : IAsyncLifetime
{
    private const int IndexerCategory = 7030;   // the category Prowlarr synced for this indexer
    private const int GlobalFallbackCategory = 8000; // KenkuSettings.IndexerComicCategories default

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly PostgresFixture _postgres = new();
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        _app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
        };
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        _server.Stop();
        await _postgres.DropDatabaseAsync(_dbName);
    }

    private const string TheBoysTorznab = """
    <?xml version="1.0" encoding="UTF-8"?>
    <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed">
      <channel>
        <item>
          <title>The Boys 001 (2006) (Digital)</title>
          <link>https://tracker.test/download/theboys001.torrent</link>
          <enclosure url="https://tracker.test/download/theboys001.torrent" type="application/x-bittorrent" />
          <torznab:attr name="seeders" value="20" />
        </item>
        <item>
          <title>The Boys 002 (2006) (Digital)</title>
          <link>https://tracker.test/download/theboys002.torrent</link>
          <enclosure url="https://tracker.test/download/theboys002.torrent" type="application/x-bittorrent" />
          <torznab:attr name="seeders" value="18" />
        </item>
      </channel>
    </rss>
    """;

    private const string EmptyTorznab =
        """<?xml version="1.0" encoding="UTF-8"?><rss version="2.0"><channel></channel></rss>""";

    /// <summary>Register a Prowlarr-synced indexer pointing at WireMock, live, the way the sync does.</summary>
    private void SyncIndexer(int[] categories)
    {
        var settings = _app.Services.GetRequiredService<KenkuSettings>();
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(
            0, "WireTracker", _server.Url! + "/api", "indexer-key", categories,
            Protocol: "torrent", Enabled: true));
    }

    private async Task<List<SearchHit>> Search(string query)
    {
        var response = await _app.CreateClient().GetAsync($"/v2/Search/Indexers/{Uri.EscapeDataString(query)}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<SearchHit>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private record SearchHit(string Name);

    [Fact]
    public async Task HappyPath_ReturnsSeriesFromIndexer_AndSendsTheIndexersOwnCategory()
    {
        _server
            .Given(Request.Create().WithPath("/api").WithParam("t", "search").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(TheBoysTorznab));
        SyncIndexer([IndexerCategory]);

        var hits = await Search("The Boys");

        // The two releases collapse into one distinct series.
        var theBoys = Assert.Single(hits);
        Assert.Equal("The Boys", theBoys.Name);

        // Regression guard: the indexer's synced category went out — NOT the global fallback.
        var sent = _server.FindLogEntries(Request.Create().WithPath("/api").UsingGet());
        var url = Assert.Single(sent).RequestMessage.Url;
        Assert.Contains($"cat={IndexerCategory}", url);
        Assert.DoesNotContain($"cat={GlobalFallbackCategory}", url);
        Assert.DoesNotContain(GlobalFallbackCategory.ToString(), url);
    }

    [Fact]
    public async Task WhenIndexerReturnsNothingForTheCategory_SearchReturnsEmptyList()
    {
        // The indexer does category filtering server-side; a query that matches nothing comes back as
        // an empty channel. The endpoint should report an empty result set, not an error.
        _server
            .Given(Request.Create().WithPath("/api").WithParam("t", "search").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(EmptyTorznab));
        SyncIndexer([IndexerCategory]);

        var hits = await Search("The Boys");

        Assert.Empty(hits);
    }

    [Fact]
    public async Task WhenIndexerRequestFails_SearchReturnsEmptyList_NotAnError()
    {
        // A failing indexer (auth/5xx/etc.) must be swallowed so one bad indexer can't sink the search.
        _server
            .Given(Request.Create().WithPath("/api").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError));
        SyncIndexer([IndexerCategory]);

        var response = await _app.CreateClient().GetAsync("/v2/Search/Indexers/The%20Boys");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hits = JsonSerializer.Deserialize<List<SearchHit>>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Empty(hits);
    }

    [Fact]
    public async Task WithNoIndexersConfigured_SearchReturnsEmptyList()
    {
        // No indexers synced or added: the search path is still registered and simply finds nothing.
        var hits = await Search("The Boys");

        Assert.Empty(hits);
    }
}
