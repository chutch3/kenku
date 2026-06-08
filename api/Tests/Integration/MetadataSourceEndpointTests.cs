using System.Net;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Activates MetadataSourceController through the real DI container — the gap that let a second
/// (test-only) constructor ship: unit tests construct the controller directly, so they never saw the
/// "multiple constructors" activation failure that 500'd every endpoint on this controller in prod.
/// </summary>
public class MetadataSourceEndpointTests : IAsyncLifetime
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
    public async Task GetMetadataSource_ActivatesControllerViaDi_DoesNotReturn500()
    {
        string key = await _app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-md-" + Guid.NewGuid().ToString("N")), "Lib");
            ctx.FileLibraries.Add(library);
            var series = new Series("Test", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(series);
            await ctx.SaveChangesAsync();
            return series.Key;
        });

        using var client = _app.CreateClient();
        var response = await client.GetAsync($"/v2/Series/{key}/metadataSource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
