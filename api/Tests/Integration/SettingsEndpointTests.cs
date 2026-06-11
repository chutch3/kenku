using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Settings endpoints through the booted app, so the real model-binding/validation pipeline runs —
/// a request record that carries validation metadata on a primary-constructor-parameter property
/// throws in the validator before the handler, which a direct unit call never reproduces.
/// </summary>
public class SettingsEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        _app = new KenkuApplicationFactory { PostgresConnectionString = _postgres.GetConnectionString(_dbName) };
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        await _postgres.DropDatabaseAsync(_dbName);
    }

    [Fact]
    public async Task GetSettings_SurfacesAnIndexerRateLimitCooldown()
    {
        var settings = _app.Services.GetRequiredService<KenkuSettings>();
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Nyaa", "https://nyaa.si", "key", [], "torrent", true));
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Calm", "https://calm.example", "key", [], "torrent", true));
        _app.Services.GetRequiredService<API.Indexers.IndexerCooldown>()
            .RecordRateLimited("Nyaa", TimeSpan.FromMinutes(10));

        using var client = _app.CreateClient();
        var json = await client.GetFromJsonAsync<JsonElement>("/v2/Settings");

        var byName = json.GetProperty("syncedIndexers").EnumerateArray()
            .ToDictionary(i => i.GetProperty("name").GetString()!);
        Assert.True(byName["Nyaa"].GetProperty("cooldownUntil").GetDateTime().ToUniversalTime() > DateTime.UtcNow.AddMinutes(5));
        Assert.Equal(JsonValueKind.Null, byName["Calm"].GetProperty("cooldownUntil").ValueKind);
    }

    [Fact]
    public async Task PatchDownloadMaxAttempts_PersistsAndReadsBack()
    {
        using var client = _app.CreateClient();

        var response = await client.PatchAsync("/v2/Settings/DownloadMaxAttempts/8", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(8, _app.Services.GetRequiredService<KenkuSettings>().DownloadMaxAttempts);

        var read = await client.GetFromJsonAsync<int>("/v2/Settings/DownloadMaxAttempts");
        Assert.Equal(8, read);
    }

    [Fact]
    public async Task PatchDownloadMaxAttempts_RejectsZero()
    {
        using var client = _app.CreateClient();

        var response = await client.PatchAsync("/v2/Settings/DownloadMaxAttempts/0", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchReleaseSelection_PersistsThroughTheRealBindingPipeline()
    {
        using var client = _app.CreateClient();

        var response = await client.PatchAsJsonAsync("/v2/Settings/ReleaseSelection",
            new { minSeeders = 1, preferredTokens = new[] { "cbz", "cbr" }, blockedTokens = new[] { "pdf" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settings = _app.Services.GetRequiredService<KenkuSettings>();
        Assert.Equal(1, settings.ReleaseMinSeeders);
        Assert.Equal(["cbz", "cbr"], settings.ReleasePreferredTokens);
        Assert.Equal(["pdf"], settings.ReleaseBlockedTokens);

        var read = await client.GetFromJsonAsync<JsonElement>("/v2/Settings/ReleaseSelection");
        Assert.Equal(1, read.GetProperty("minSeeders").GetInt32());
    }
}
