using API.DownloadClients.Interfaces;
using API.JobRuntime.Handlers;
using API.JobRuntime.Interfaces;
using API.DownloadClients;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

/// <summary>
/// Deleting a series must sweep its operational residue too: outstanding jobs for the series are
/// removed (they would otherwise retry into ghost NeedsAttention rows against vanished SourceIds)
/// and its tagged torrents are released from the download client. The Invincible incident.
/// </summary>
public class SeriesDeletionEndToEndTests : IAsyncLifetime
{
    private sealed class RecordingDownloadClient : IDownloadClient
    {
        public readonly List<string> Removed = [];
        public Task<string?> Add(string downloadUrl, string saveDir, string tag, CancellationToken ct) => Task.FromResult<string?>(tag);
        public Task<DownloadStatus?> GetStatus(string tag, CancellationToken ct) => Task.FromResult<DownloadStatus?>(null);
        public Task Remove(string tag, bool deleteData, CancellationToken ct)
        {
            Removed.Add(tag);
            return Task.CompletedTask;
        }
    }

    private readonly PostgresFixture _postgres = new();
    private readonly RecordingDownloadClient _client = new();
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        _app = new KenkuApplicationFactory
        {
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
            ExtraServices = services => services.AddSingleton<IDownloadClient>(_client),
        };
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        await _postgres.DropDatabaseAsync(_dbName);
    }

    [Fact]
    public async Task DeletingASeries_RemovesItsJobs_AndReleasesItsTorrents()
    {
        (string mangaId, string chapterSourceKey) = await _app.WithSeriesContext(async ctx =>
        {
            var manga = new Series("Invincible", "", "", SeriesReleaseStatus.Continuing, [], [], [], []);
            ctx.Series.Add(manga);
            var chapter = new Chapter(manga, "106", null, null);
            ctx.Chapters.Add(chapter);
            var sourceId = new SourceId<Chapter>(chapter, "Indexers", "106", null, true);
            ctx.MangaConnectorToChapter.Add(sourceId);
            await ctx.SaveChangesAsync();
            return (manga.Key, sourceId.Key);
        });

        using (var scope = _app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            await store.EnqueueAsync(new JobEntity(DownloadChapterHandler.Type, "{}", DateTime.UtcNow, resourceKey: mangaId));
            var broken = await store.EnqueueAsync(new JobEntity(SyncSeriesChaptersHandler.Type, "{}", DateTime.UtcNow, resourceKey: mangaId));
            broken.Status = JobStatus.NeedsAttention;
            broken.Error = "SourceId not found";
            await store.UpdateAsync(broken);
        }

        var response = await _app.CreateClient().DeleteAsync($"/v2/Series/{mangaId}");
        response.EnsureSuccessStatusCode();

        Assert.Empty(await _app.WithJobsContext(c => c.JobQueue.Where(j => j.ResourceKey == mangaId).ToListAsync()));
        Assert.Contains(chapterSourceKey, _client.Removed);
    }

    // Only torrent-kind sources tag the download client; asking it about every scrape/archive chapter
    // turned deletion into one HTTP round-trip per chapter (the slow-delete incident).
    [Fact]
    public async Task DeletingASeries_OnlyReleasesTorrentTaggedChapters()
    {
        (string mangaId, string torrentKey, string scrapeKey) = await _app.WithSeriesContext(async ctx =>
        {
            var manga = new Series("The Boys", "", "", SeriesReleaseStatus.Completed, [], [], [], []);
            ctx.Series.Add(manga);
            var torrentChapter = new Chapter(manga, "1", null, null);
            var scrapeChapter = new Chapter(manga, "2", null, null);
            ctx.Chapters.AddRange(torrentChapter, scrapeChapter);
            var torrentId = new SourceId<Chapter>(torrentChapter, "Indexers", "1", null, true);
            var scrapeId = new SourceId<Chapter>(scrapeChapter, "WeebCentral", "2", null, true);
            ctx.MangaConnectorToChapter.AddRange(torrentId, scrapeId);
            await ctx.SaveChangesAsync();
            return (manga.Key, torrentId.Key, scrapeId.Key);
        });

        var response = await _app.CreateClient().DeleteAsync($"/v2/Series/{mangaId}");
        response.EnsureSuccessStatusCode();

        Assert.Contains(torrentKey, _client.Removed);
        Assert.DoesNotContain(scrapeKey, _client.Removed);
    }

    // Connector-name lookups are case-insensitive everywhere else; the torrent-tag filter must agree.
    [Fact]
    public async Task DeletingASeries_MatchesTorrentSourcesCaseInsensitively()
    {
        (string mangaId, string torrentKey) = await _app.WithSeriesContext(async ctx =>
        {
            var manga = new Series("Preacher", "", "", SeriesReleaseStatus.Completed, [], [], [], []);
            ctx.Series.Add(manga);
            var chapter = new Chapter(manga, "1", null, null);
            ctx.Chapters.Add(chapter);
            var sourceId = new SourceId<Chapter>(chapter, "INDEXERS", "1", null, true);
            ctx.MangaConnectorToChapter.Add(sourceId);
            await ctx.SaveChangesAsync();
            return (manga.Key, sourceId.Key);
        });

        var response = await _app.CreateClient().DeleteAsync($"/v2/Series/{mangaId}");
        response.EnsureSuccessStatusCode();

        Assert.Contains(torrentKey, _client.Removed);
    }
}
