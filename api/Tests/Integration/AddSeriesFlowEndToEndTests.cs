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
        c.MangaConnectorToManga.Include(id => id.Obj).SingleAsync(id => id.Obj.Name == "Fire Punch"));

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
