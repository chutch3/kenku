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
