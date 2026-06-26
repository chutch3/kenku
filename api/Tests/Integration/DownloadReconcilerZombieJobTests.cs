using API.JobRuntime.Handlers;
using API.JobRuntime.Interfaces;
using API.JobRuntime.Reconcilers;
using API.Schema.ActionsContext;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;
using JobsContext = API.Schema.JobsContext.JobsContext;
using SeriesRollup = API.Controllers.DTOs.SeriesRollup;

namespace API.Tests.Integration;

/// <summary>
/// End-to-end proof of the zombie-job fix on the real stack (Postgres + EfJobStore + the rollup the badge
/// reads), not the in-memory fakes. A chapter that is downloaded but still carries a parked download
/// failure — with a STALE dedup key, mirroring the real post-migration shape — must, after one reconciler
/// tick, leave the series reporting zero needs-attention jobs (the observable outcome), with the dead job
/// retired. The companion still-missing case keeps its parked job.
/// </summary>
[Collection("postgres")]
public class DownloadReconcilerZombieJobTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _pg.CreateDatabaseAsync();
        _app = new KenkuApplicationFactory { PostgresConnectionString = _pg.GetConnectionString(_dbName) };
        _ = _app.Services; // build the host → the factory migrates every context
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        await _pg.DropDatabaseAsync(_dbName);
    }

    private static async Task<SeriesRollup> RollupFor(IServiceScope scope, string seriesKey) =>
        (await scope.ServiceProvider.GetRequiredService<SeriesRollupService>().GetAsync(
            scope.ServiceProvider.GetRequiredService<SeriesContext>(),
            scope.ServiceProvider.GetRequiredService<JobsContext>(),
            scope.ServiceProvider.GetRequiredService<ActionsContext>(), default))
        .Single(r => r.SeriesId == seriesKey);

    [Fact]
    public async Task ReconcilerTick_RetiresTheParkedFailureAndClearsTheBadge_WhenTheChapterIsDownloaded()
    {
        string seriesKey;

        using (var scope = _app.Services.CreateScope())
        {
            var series = scope.ServiceProvider.GetRequiredService<SeriesContext>();
            var lib = new FileLibrary(Path.GetTempPath(), "Lib");
            var manga = new Series("Crossed-like", "", "u", SeriesReleaseStatus.Completed, [], [], [], [], lib);
            var chapter = new Chapter(manga, "9", null) { Downloaded = true }; // already on disk
            var sourceId = new SourceId<Chapter>(chapter, "ComicHubFree", "crossed/issue-9", "u", useForDownload: true);
            series.FileLibraries.Add(lib);
            series.Series.Add(manga);
            series.Chapters.Add(chapter);
            series.ChapterSourceIds.Add(sourceId);
            await series.SaveChangesAsync();
            seriesKey = manga.Key;

            // A download that failed earlier and parked — carrying a STALE dedup key that matches no current
            // chapter (the real shape after the source-id re-key migration).
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            var job = await store.EnqueueAsync(new JobEntity(DownloadChapterHandler.Type,
                DownloadChapterHandler.PayloadFor(sourceId.Key), DateTime.UtcNow,
                resourceKey: seriesKey, dedupKey: "download:stale-old-key"));
            job.Status = JobStatus.NeedsAttention;
            job.Error = "none of the 33 page image(s) could be downloaded";
            await store.UpdateAsync(job);
        }

        using (var scope = _app.Services.CreateScope())
        {
            var before = await RollupFor(scope, seriesKey);
            Assert.Equal(1, before.DownloadedChapters);
            Assert.Equal(1, before.NeedsAttentionJobs); // the badge shows "Needs attention"
        }

        using (var scope = _app.Services.CreateScope())
            await DownloadReconciler.ScanAndEnqueueAsync(
                scope.ServiceProvider.GetRequiredService<SeriesContext>(),
                scope.ServiceProvider.GetRequiredService<IJobStore>(),
                DateTime.UtcNow, null, [], 5, default);

        using (var scope = _app.Services.CreateScope())
        {
            var after = await RollupFor(scope, seriesKey);
            Assert.Equal(0, after.NeedsAttentionJobs); // observable outcome: badge clears to "Up to date"

            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            var download = Assert.Single((await store.GetAllAsync()).Where(j => j.Type == DownloadChapterHandler.Type));
            Assert.Equal(JobStatus.Cancelled, download.Status); // the zombie is retired in the real store
        }
    }

    [Fact]
    public async Task ReconcilerTick_KeepsTheParkedFailure_WhenTheChapterIsStillMissing()
    {
        string seriesKey;

        using (var scope = _app.Services.CreateScope())
        {
            var series = scope.ServiceProvider.GetRequiredService<SeriesContext>();
            var lib = new FileLibrary(Path.GetTempPath(), "Lib");
            var manga = new Series("Still-missing", "", "u", SeriesReleaseStatus.Completed, [], [], [], [], lib);
            var chapter = new Chapter(manga, "1", null); // NOT downloaded
            var sourceId = new SourceId<Chapter>(chapter, "ComicHubFree", "still/issue-1", "u", useForDownload: true);
            series.FileLibraries.Add(lib);
            series.Series.Add(manga);
            series.Chapters.Add(chapter);
            series.ChapterSourceIds.Add(sourceId);
            await series.SaveChangesAsync();
            seriesKey = manga.Key;

            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            var job = await store.EnqueueAsync(new JobEntity(DownloadChapterHandler.Type,
                DownloadChapterHandler.PayloadFor(sourceId.Key), DateTime.UtcNow,
                resourceKey: seriesKey, dedupKey: DownloadReconciler.DedupKey(sourceId.Key)));
            job.Status = JobStatus.NeedsAttention;
            await store.UpdateAsync(job);
        }

        using (var scope = _app.Services.CreateScope())
            await DownloadReconciler.ScanAndEnqueueAsync(
                scope.ServiceProvider.GetRequiredService<SeriesContext>(),
                scope.ServiceProvider.GetRequiredService<IJobStore>(),
                DateTime.UtcNow, null, [], 5, default);

        using (var scope = _app.Services.CreateScope())
        {
            var after = await RollupFor(scope, seriesKey);
            Assert.Equal(1, after.NeedsAttentionJobs); // a real failure is not silently swept away
        }
    }
}
