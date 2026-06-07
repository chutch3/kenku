using System.Net;
using API.JobRuntime.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// AF5: PATCH DownloadFrom/true enqueues both a DownloadCover and a SyncSeriesChapters job.
/// </summary>
[Trait("Category", "Integration")]
public class SeriesRefreshEndToEndTests()
    : OutboundHttpIntegrationTest(ConnectorReturning(IntegrationFixtures.WeebCentralFirePunchHtml))
{
    [Fact]
    public async Task MarkAsRequested_EnqueuesDownloadCoverAndSyncChaptersJobs()
    {
        string libraryKey = await SeedLibrary();

        using var client = App.CreateClient();

        var importResponse = await client.PostAsync(
            $"/v2/Series/unknown-id/ChangeLibrary/{libraryKey}?connectorName=WeebCentral&connectorSeriesId=wc-1", null);
        importResponse.EnsureSuccessStatusCode();

        var series = await App.WithSeriesContext(ctx =>
            ctx.Series.Include(m => m.SourceIds).FirstAsync());

        var mcId = series.SourceIds.First(id => id.MangaConnectorName == "WeebCentral");

        var patchResponse = await client.PatchAsync(
            $"/v2/Series/{series.Key}/DownloadFrom/WeebCentral/true", null);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        var hasCoverJob = await App.WithJobsContext(ctx =>
            ctx.JobQueue.AnyAsync(j => j.Type == DownloadCoverHandler.Type));
        var hasSyncJob = await App.WithJobsContext(ctx =>
            ctx.JobQueue.AnyAsync(j => j.Type == SyncSeriesChaptersHandler.Type));

        Assert.True(hasCoverJob, "MarkAsRequested must enqueue a DownloadCover job");
        Assert.True(hasSyncJob, "MarkAsRequested must enqueue a SyncSeriesChapters job");
    }
}
