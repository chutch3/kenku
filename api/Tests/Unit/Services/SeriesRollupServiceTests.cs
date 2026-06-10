using API.JobRuntime.Handlers;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Unit.Services;

public class SeriesRollupServiceTests : IDisposable
{
    private readonly SeriesContext _series = new(new DbContextOptionsBuilder<SeriesContext>()
        .UseInMemoryDatabase("rollup-" + Guid.NewGuid().ToString("N")).Options);
    private readonly JobsContext _jobs = new(new DbContextOptionsBuilder<JobsContext>()
        .UseInMemoryDatabase("rollup-jobs-" + Guid.NewGuid().ToString("N")).Options);
    private readonly ActionsContext _actions = new(new DbContextOptionsBuilder<ActionsContext>()
        .UseInMemoryDatabase("rollup-actions-" + Guid.NewGuid().ToString("N")).Options);

    public void Dispose()
    {
        _series.Dispose();
        _jobs.Dispose();
        _actions.Dispose();
    }

    private async Task<Series> SeedSeries(string name)
    {
        var manga = new Series(name, "", "", SeriesReleaseStatus.Continuing, [], [], [], []);
        _series.Series.Add(manga);
        await _series.SaveChangesAsync();
        return manga;
    }

    [Fact]
    public async Task ASeriesWithNoChaptersOrJobs_StillGetsAZeroRollupRow()
    {
        var manga = await SeedSeries("Empty");

        var rollups = await new SeriesRollupService().GetAsync(_series, _jobs, _actions, CancellationToken.None);

        var rollup = Assert.Single(rollups, r => r.MangaId == manga.Key);
        Assert.Equal(0, rollup.WantedChapters);
        Assert.Equal(0, rollup.DownloadedChapters);
        Assert.Null(rollup.LastError);
        Assert.Null(rollup.LastSyncAt);
    }

    [Fact]
    public async Task LastError_IsTheMostRecentFailure_NotTheFirst()
    {
        var manga = await SeedSeries("Saga");
        var older = new Job(SyncSeriesChaptersHandler.Type, "{}", new DateTime(2026, 1, 1), resourceKey: manga.Key)
            { Status = JobStatus.NeedsAttention, Error = "older error", FinishedAt = new DateTime(2026, 1, 1) };
        var newer = new Job(SyncSeriesChaptersHandler.Type, "{}", new DateTime(2026, 2, 1), resourceKey: manga.Key)
            { Status = JobStatus.NeedsAttention, Error = "newer error", FinishedAt = new DateTime(2026, 2, 1) };
        _jobs.JobQueue.AddRange(older, newer);
        await _jobs.SaveChangesAsync();

        var rollups = await new SeriesRollupService().GetAsync(_series, _jobs, _actions, CancellationToken.None);

        Assert.Equal("newer error", Assert.Single(rollups).LastError);
    }

    [Fact]
    public async Task LastSync_IsTheLatestRetrievedRecord_WithItsCount()
    {
        var manga = await SeedSeries("Saga");
        _actions.Actions.Add(new ChaptersRetrievedActionRecord(manga, 3) { PerformedAt = new DateTime(2026, 1, 1) });
        _actions.Actions.Add(new ChaptersRetrievedActionRecord(manga, 22) { PerformedAt = new DateTime(2026, 2, 1) });
        await _actions.SaveChangesAsync();

        var rollups = await new SeriesRollupService().GetAsync(_series, _jobs, _actions, CancellationToken.None);

        var rollup = Assert.Single(rollups);
        Assert.Equal(new DateTime(2026, 2, 1), rollup.LastSyncAt);
        Assert.Equal(22, rollup.LastSyncChapterCount);
    }
}
