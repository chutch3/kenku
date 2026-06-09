using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using API.Controllers.DTOs;
using API.JobRuntime.Handlers;
using API.JobRuntime.Interfaces;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;
using Chapter = API.Schema.SeriesContext.Chapter;
using FileLibrary = API.Schema.SeriesContext.FileLibrary;
using Series = API.Schema.SeriesContext.Series;

namespace API.Tests.Integration;

/// <summary>
/// AF6a: the per-series rollup — actual download progress, job counts, and the last failure — so the
/// library badge can tell the truth ('Downloading' used to mean only 'a source is enabled').
/// </summary>
public class SeriesRollupEndToEndTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    private static readonly JsonSerializerOptions Json = BuildJson();
    private static JsonSerializerOptions BuildJson()
    {
        var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

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
    public async Task Rollup_ReportsProgress_JobCounts_AndTheLastFailure_PerSeries()
    {
        string seriesKey = await _app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary("/tmp/rollup-lib", "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Saga", "", "", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(manga);
            for (int i = 1; i <= 3; i++)
            {
                var chapter = new Chapter(manga, i.ToString(), null, null) { Downloaded = i == 1 };
                ctx.Chapters.Add(chapter);
                ctx.MangaConnectorToChapter.Add(new API.Schema.SeriesContext.SourceId<Chapter>(chapter, "Src", $"c{i}", null, true));
            }
            await ctx.SaveChangesAsync();
            return manga.Key;
        });

        using (var scope = _app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            await store.EnqueueAsync(new JobEntity(DownloadChapterHandler.Type, "{}", DateTime.UtcNow, resourceKey: seriesKey));
            await store.EnqueueAsync(new JobEntity(DownloadChapterHandler.Type, "{}", DateTime.UtcNow, resourceKey: seriesKey));
            var broken = await store.EnqueueAsync(new JobEntity(SyncSeriesChaptersHandler.Type, "{}", DateTime.UtcNow, resourceKey: seriesKey));
            broken.Status = JobStatus.NeedsAttention;
            broken.Error = "chapter list request failed: HTTP 404";
            await store.UpdateAsync(broken);
        }

        using (var scope = _app.Services.CreateScope())
        {
            var actions = scope.ServiceProvider.GetRequiredService<ActionsContext>();
            var series = await _app.WithSeriesContext(c => Task.FromResult(c.Series.Single(s => s.Key == seriesKey)));
            actions.Actions.Add(new ChaptersRetrievedActionRecord(series, 3));
            await actions.SaveChangesAsync();
        }

        using var client = _app.CreateClient();
        var rollups = JsonSerializer.Deserialize<List<SeriesRollup>>(
            await client.GetStringAsync("/v2/Series/Rollup"), Json)!;

        SeriesRollup rollup = Assert.Single(rollups, r => r.MangaId == seriesKey);
        Assert.Equal(3, rollup.WantedChapters);
        Assert.Equal(1, rollup.DownloadedChapters);
        Assert.Equal(2, rollup.QueuedJobs);
        Assert.Equal(0, rollup.RunningJobs);
        Assert.Equal(1, rollup.NeedsAttentionJobs);
        Assert.Contains("404", rollup.LastError);
        Assert.NotNull(rollup.LastSyncAt);
        Assert.Equal(3, rollup.LastSyncChapterCount);
    }
}
