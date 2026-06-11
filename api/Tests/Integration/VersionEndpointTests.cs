using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace API.Tests.Integration;

/// <summary>The deployed version must be discoverable from the GUI, so the API exposes its build identity.</summary>
public class VersionEndpointTests : IAsyncLifetime
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
    public async Task GetVersion_ReturnsTheBuildIdentity()
    {
        using var client = _app.CreateClient();

        var json = await client.GetFromJsonAsync<JsonElement>("/v2/Version");

        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("commit").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("builtAt").GetString()));
    }
}
