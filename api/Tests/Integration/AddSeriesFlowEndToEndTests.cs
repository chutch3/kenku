using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// The one-decision add flow behind the search-page modal: ChangeLibrary with download=true tracks the
/// series, enables the originating source, and queues cover + chapter sync in a single call; download
/// absent/false tracks it as a watchlist entry — chapters still sync (so the list populates) but the
/// source stays off and nothing downloads.
/// </summary>
[Trait("Category", "Integration")]
public class AddSeriesFlowEndToEndTests() : OutboundHttpIntegrationTest(ConnectorReturning(IntegrationFixtures.WeebCentralFirePunchHtml))
{
    private Task<SourceId<Series>> FirePunchSource() => App.WithSeriesContext(c =>
        c.SeriesSourceIds.Include(id => id.Obj).SingleAsync(id => id.Obj.Name == "Fire Punch"));

    private Task<List<API.Schema.JobsContext.Job>> Jobs() => App.WithJobsContext(c => c.JobQueue.ToListAsync());

    [Fact]
    public async Task AddAndDownload_EnablesTheSource_AndQueuesCoverAndSync()
    {
        string libraryKey = await SeedLibrary();

        var response = await App.CreateClient().PostAsync(
            $"/v2/Series/unknown/ChangeLibrary/{libraryKey}?connectorName=WeebCentral&connectorSeriesId=wc-1&download=true", null);
        response.EnsureSuccessStatusCode();

        SourceId<Series> source = await FirePunchSource();
        Assert.True(source.UseForDownload, "Add & download must enable the source it was added from");
        List<API.Schema.JobsContext.Job> jobs = await Jobs();
        Assert.Contains(jobs, j => j.Type == SyncSeriesChaptersHandler.Type);
        Assert.Contains(jobs, j => j.Type == DownloadCoverHandler.Type);
    }

    [Fact]
    public async Task SyncNow_QueuesChapterSyncAndCover_ForTheSeriesSources()
    {
        string libraryKey = await SeedLibrary();
        var add = await App.CreateClient().PostAsync(
            $"/v2/Series/unknown/ChangeLibrary/{libraryKey}?connectorName=WeebCentral&connectorSeriesId=wc-1&download=true", null);
        add.EnsureSuccessStatusCode();
        string seriesId = (await FirePunchSource()).ObjId;
        await App.WithJobsContext(async c => { c.JobQueue.RemoveRange(c.JobQueue); return await c.SaveChangesAsync(); });

        var response = await App.CreateClient().PostAsync($"/v2/Series/{seriesId}/Sync", null);
        response.EnsureSuccessStatusCode();

        List<API.Schema.JobsContext.Job> jobs = await Jobs();
        Assert.Contains(jobs, j => j.Type == SyncSeriesChaptersHandler.Type);
        Assert.Contains(jobs, j => j.Type == DownloadCoverHandler.Type);
    }

    [Fact]
    public async Task AddAndDownload_DoesNotCrash_WhenMergingIntoAnExistingSeriesWithChapters()
    {
        // Re-adding a series that already exists (unknown key → GetSeriesFromId → UpsertSeries merge)
        // loads the existing chapters via SeriesIncludeAll, which doesn't ThenInclude chapter SourceIds
        // — so under SplitQuery they're null. The download=true source-enable loop dereferenced that,
        // a bare 500 (the real WeebCentral re-add crash).
        string libraryKey = await App.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-nullsrc-" + Guid.NewGuid().ToString("N")), "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Fire Punch", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library)
                { IsTracked = true };
            manga.SourceIds.Add(new SourceId<Series>(manga, "WeebCentral", "wc-1", "https://weebcentral.com/series/wc-1"));
            ctx.Series.Add(manga);
            ctx.Chapters.Add(new Chapter(manga, "1", null, null));
            await ctx.SaveChangesAsync();
            return library.Key;
        });

        var response = await App.CreateClient().PostAsync(
            $"/v2/Series/unknown/ChangeLibrary/{libraryKey}?connectorName=WeebCentral&connectorSeriesId=wc-1&download=true", null);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AddOnly_TracksWithoutEnablingTheSource_ButStillSyncsTheChapterList()
    {
        string libraryKey = await SeedLibrary();

        var response = await App.CreateClient().PostAsync(
            $"/v2/Series/unknown/ChangeLibrary/{libraryKey}?connectorName=WeebCentral&connectorSeriesId=wc-1", null);
        response.EnsureSuccessStatusCode();

        SourceId<Series> source = await FirePunchSource();
        Assert.False(source.UseForDownload, "Add only must not start downloads");
        Assert.Contains(await Jobs(), j => j.Type == SyncSeriesChaptersHandler.Type);
    }
}
