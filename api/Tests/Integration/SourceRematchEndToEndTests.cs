using System.Net.Http.Json;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// The intervention loop's repair step: when a stored connector id is wrong (the "I am a hero" case —
/// sync fails or yields nothing, and remove-and-re-add just re-stores the same bad id), the user
/// re-matches the source to the right entry. The link is REPLACED (its key derives from the site id),
/// download preference carries over, and a fresh sync is queued immediately.
/// </summary>
[Trait("Category", "Integration")]
public class SourceRematchEndToEndTests() : OutboundHttpIntegrationTest(ConnectorReturning(IntegrationFixtures.WeebCentralFirePunchHtml))
{
    [Fact]
    public async Task Rematch_ReplacesTheSourceLink_KeepsDownloadPreference_AndQueuesASync()
    {
        string libraryKey = await SeedLibrary();
        (string mangaId, string oldSourceKey) = await App.WithSeriesContext(async ctx =>
        {
            var library = await ctx.FileLibraries.FindAsync(libraryKey);
            var manga = new Series("I Am A Hero", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            var source = new SourceId<Series>(manga, "WeebCentral", "01ABC/I-Am-A-Hero", null, true);
            manga.SourceIds.Add(source);
            ctx.Series.Add(manga);
            await ctx.SaveChangesAsync();
            return (manga.Key, source.Key);
        });

        var response = await App.CreateClient().PostAsJsonAsync(
            $"/v2/Series/{mangaId}/Source/{oldSourceKey}/Rematch",
            new { idOnConnectorSite = "01ABC", websiteUrl = "https://weebcentral.com/series/01ABC" });
        response.EnsureSuccessStatusCode();

        var sources = await App.WithSeriesContext(c =>
            c.MangaConnectorToManga.Where(id => id.ObjId == mangaId).ToListAsync());
        SourceId<Series> replacement = Assert.Single(sources);
        Assert.Equal("01ABC", replacement.IdOnConnectorSite);
        Assert.Equal("WeebCentral", replacement.MangaConnectorName);
        Assert.True(replacement.UseForDownload, "download preference must survive the re-match");
        Assert.NotEqual(oldSourceKey, replacement.Key);

        var jobs = await App.WithJobsContext(c => c.JobQueue.ToListAsync());
        var sync = Assert.Single(jobs, j => j.Type == SyncSeriesChaptersHandler.Type);
        var payload = System.Text.Json.JsonSerializer.Deserialize<SyncSeriesChaptersPayload>(sync.Payload)!;
        Assert.Equal(replacement.Key, payload.SourceIdKey);
    }

    [Fact]
    public async Task Rematch_OnAForeignSeries_IsNotFound()
    {
        string libraryKey = await SeedLibrary();
        string sourceKey = await App.WithSeriesContext(async ctx =>
        {
            var library = await ctx.FileLibraries.FindAsync(libraryKey);
            var manga = new Series("Other", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            var source = new SourceId<Series>(manga, "WeebCentral", "x", null);
            manga.SourceIds.Add(source);
            ctx.Series.Add(manga);
            await ctx.SaveChangesAsync();
            return source.Key;
        });

        var response = await App.CreateClient().PostAsJsonAsync(
            $"/v2/Series/not-that-series/Source/{sourceKey}/Rematch", new { idOnConnectorSite = "y" });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
