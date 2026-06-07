using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Outside-in coverage of the whole feature as one chain: import a series from a connector (which captures
/// its AniList link), then resolve volumes — the captured link must drive an identifier match to the right
/// MangaDex entry over a higher-scoring decoy. The link is never preset, so this exercises the seam between
/// capture and matching. Only the two HTTP edges are stubbed (connector + MangaDex).
/// </summary>
[Trait("Category", "Integration")]
public class VolumeMatchFromImportEndToEndTests() : OutboundHttpIntegrationTest(ConnectorReturning(IntegrationFixtures.WeebCentralFirePunchHtml))
{
    [Fact]
    public async Task ImportedSeries_MatchesMangaDexByItsCapturedAniListLink_OverAHigherScoringDecoy()
    {
        StubMangaDexSearch(IntegrationFixtures.DecoyVsTrueSearch);
        StubMangaDexAggregate(IntegrationFixtures.AggregateChapter1Volume1);
        StubWikipediaEmpty();

        string libraryKey = await SeedLibrary();

        // 1. Import the series from the connector — this is where the AniList link is captured.
        (await App.CreateClient().PostAsync(
            $"/v2/Series/unknown/ChangeLibrary/{libraryKey}?connectorName=WeebCentral&connectorSeriesId=wc-1", null))
            .EnsureSuccessStatusCode();

        // 2. Give it a downloaded chapter so there is something to resolve.
        string mangaKey = await App.WithSeriesContext(async ctx =>
        {
            var manga = await ctx.Series.FirstAsync(m => m.Name == "Fire Punch");
            ctx.Chapters.Add(new Chapter(manga, "1", null, null) { Downloaded = true, FileName = "Ch.1.cbz" });
            await ctx.SaveChangesAsync();
            return manga.Key;
        });

        // 3. Resolve — the captured link must pick true-uuid even though the decoy wins on title + count.
        (await App.CreateClient().PostAsync("/v2/Maintenance/ResolveMissingVolumes", null)).EnsureSuccessStatusCode();
        await DrainJobsAsync();

        Assert.Equal("true-uuid", (await MetadataSourceFor(mangaKey)).ExternalId);
    }
}
