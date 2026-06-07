using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Outside-in: a series is imported through the actual add-to-library HTTP endpoint, so the request flows
/// controller -> WeebCentral connector -> UpsertManga -> EF. The only thing stubbed is the connector's HTTP
/// edge (the injected <see cref="API.HttpRequesters.Interfaces.IHttpRequester"/>). Asserts the external links the
/// connector surfaced actually round-tripped to the database.
/// </summary>
[Trait("Category", "Integration")]
public class ConnectorLinkCaptureEndToEndTests() : OutboundHttpIntegrationTest(ConnectorReturning(IntegrationFixtures.WeebCentralFirePunchHtml))
{
    private Task<List<Series>> FirePunchSeries() => App.WithSeriesContext(c =>
        c.Series.Include(m => m.Links).Where(m => m.Name == "Fire Punch").ToListAsync());

    [Fact]
    public async Task ImportingWeebCentralSeries_PersistsItsExternalLinks_OverTheRealStack()
    {
        string libraryKey = await SeedLibrary();

        var response = await App.CreateClient().PostAsync(
            $"/v2/Series/unknown-id/ChangeLibrary/{libraryKey}?connectorName=WeebCentral&connectorSeriesId=wc-1", null);
        response.EnsureSuccessStatusCode();

        var series = await FirePunchSeries();
        Assert.Single(series);
        Assert.Contains(series[0].Links, l => l.LinkUrl == IntegrationFixtures.FirePunchAniListUrl);
    }

    [Fact]
    public async Task ReimportingExistingSeries_BackfillsItsExternalLinks_WithoutDuplicating()
    {
        // A series imported before link-capture existed has no links. Re-importing it must backfill the
        // links onto the SAME series (UpsertManga merge path), not drop them or create a duplicate.
        string libraryKey = await SeedLibrary();
        await App.WithSeriesContext(async ctx =>
        {
            var library = await ctx.FileLibraries.FindAsync(libraryKey);
            var manga = new Series("Fire Punch", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            manga.SourceIds.Add(new SourceId<Series>(manga, "WeebCentral", "wc-1", "https://weebcentral.com/series/wc-1", true));
            ctx.Series.Add(manga);
            await ctx.SaveChangesAsync();
            return 0;
        });

        var response = await App.CreateClient().PostAsync(
            $"/v2/Series/unknown-id/ChangeLibrary/{libraryKey}?connectorName=WeebCentral&connectorSeriesId=wc-1", null);
        response.EnsureSuccessStatusCode();

        var series = await FirePunchSeries();
        Assert.Single(series);
        Assert.Contains(series[0].Links, l => l.LinkUrl == IntegrationFixtures.FirePunchAniListUrl);
    }
}
