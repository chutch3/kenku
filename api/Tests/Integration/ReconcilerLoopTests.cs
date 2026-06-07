using API.JobRuntime.Handlers;
using API.Schema.ActionsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Proves that DownloadReconciler.ExecuteAsync → ScanAndEnqueueAsync wiring works end-to-end:
/// when the app starts with RunStartup=true and Postgres has a chapter pending download, the reconciler
/// enqueues a DownloadChapter job on its first tick (before Task.Delay). DispatcherCaps=(0,0) prevents
/// the job pool from executing anything, isolating reconciler behaviour from handler execution.
/// </summary>
public class ReconcilerLoopTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private readonly string _libDir = Path.Combine(Path.GetTempPath(), "kenku-rec-" + Guid.NewGuid().ToString("N"));
    private string? _dbName;
    private KenkuApplicationFactory? _app;

    public async Task InitializeAsync()
    {
        if (!await _postgres.IsReachableAsync()) return;
        _dbName = await _postgres.CreateDatabaseAsync();
        string cs = _postgres.GetConnectionString(_dbName);
        Directory.CreateDirectory(_libDir);

        await MigrateAsync<SeriesContext>(cs);
        await MigrateAsync<global::API.Schema.JobsContext.JobsContext>(cs);
        await MigrateAsync<ActionsContext>(cs);

        var opts = new DbContextOptionsBuilder<SeriesContext>().UseNpgsql(cs).Options;
        await using (var ctx = new SeriesContext(opts))
        {
            var library = new FileLibrary(_libDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Test", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(manga);
            var chapter = new Chapter(manga, "1", null, null);
            ctx.Chapters.Add(chapter);
            var sourceId = new SourceId<Chapter>(chapter, "StubConnector", "site-id-1", "http://stub.test/1", true);
            ctx.MangaConnectorToChapter.Add(sourceId);
            await ctx.SaveChangesAsync();
        }

        _app = new KenkuApplicationFactory
        {
            PostgresConnectionString = cs,
            RunStartup = true,
            DispatcherCaps = (0, 0),
        };
    }

    private static async Task MigrateAsync<TContext>(string cs) where TContext : DbContext
    {
        var opts = new DbContextOptionsBuilder<TContext>().UseNpgsql(cs).Options;
        await using var ctx = (TContext)Activator.CreateInstance(typeof(TContext), opts)!;
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _app?.Dispose();
        try { Directory.Delete(_libDir, recursive: true); } catch { }
        if (_dbName is not null)
            await _postgres.DropDatabaseAsync(_dbName);
    }

    [Fact]
    public async Task DownloadReconciler_OnFirstTick_EnqueuesDownloadJobForRequestedChapter()
    {
        if (_app is null) return; // skip: Postgres not available

        bool appeared = false;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var jobs = await _app.WithJobsContext(ctx => ctx.JobQueue.ToListAsync());
            if (jobs.Any(j => j.Type == DownloadChapterHandler.Type))
            {
                appeared = true;
                break;
            }
            await Task.Delay(100);
        }

        Assert.True(appeared, "DownloadReconciler should have enqueued a DownloadChapter job on its first tick");
    }
}
