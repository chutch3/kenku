using API;
using API.MangaConnectors;
using API.Schema.SeriesContext;
using API.Workers;
using API.Workers.MangaDownloadWorkers;
using API.Workers.PeriodicWorkers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace API.Tests.Workers;

public class StartNewChapterDownloadsWorkerTests
{
    private static (SeriesContext context, SourceId<Chapter> chapterId, SeriesSource connector, KenkuSettings settings)
        SetupMissingChapter(string dbName)
    {
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var context = new SeriesContext(options);

        var library = new FileLibrary("/tmp/manga", "Test Lib");
        context.FileLibraries.Add(library);

        var manga = new Series("Test Series", "Desc", "http://cover.com", SeriesReleaseStatus.Continuing,
            new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
            library, 0f, 2024, "en");
        context.Series.Add(manga);

        var chapter = new Chapter(manga, "1", null, "Title");
        context.Chapters.Add(chapter);

        var chapterId = new SourceId<Chapter>(chapter, "MockConnector", "site1", "url1", true);
        context.MangaConnectorToChapter.Add(chapterId);
        context.SaveChanges();

        var settings = new KenkuSettings { AppData = "/tmp", MaxConcurrentDownloads = 5 };
        var connector = new Mock<SeriesSource>("MockConnector", new[] { "en" }, new[] { "mock.com" }, "icon", settings).Object;

        return (context, chapterId, connector, settings);
    }

    private static IServiceScope ScopeFor(SeriesContext context)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        return services.BuildServiceProvider().CreateScope();
    }

    [Fact]
    public async Task DoWork_DoesNotDuplicate_ChapterAlreadyQueuedButNotYetRunning()
    {
        var (context, chapterId, connector, settings) = SetupMissingChapter(nameof(DoWork_DoesNotDuplicate_ChapterAlreadyQueuedButNotYetRunning));

        // A download worker for this chapter exists in the queue but has NOT started yet
        // (i.e. it is "known" but not "running"). The periodic worker must not schedule a duplicate.
        var alreadyQueued = new DownloadChapterFromSourceWorker(chapterId, new[] { connector }, settings);

        var queue = new Mock<IWorkerQueue>();
        queue.Setup(q => q.GetKnownWorkers()).Returns([alreadyQueued]);
        queue.Setup(q => q.GetRunningWorkers()).Returns([]); // not running yet

        var worker = new StartNewChapterDownloadsWorker(settings, queue.Object, new[] { connector });

        BaseWorker[] created = await worker.DoWork(ScopeFor(context));

        Assert.DoesNotContain(created.OfType<DownloadChapterFromSourceWorker>(),
            w => w.ChapterIdId == chapterId.Key);
    }

    [Fact]
    public async Task DoWork_SchedulesDownload_WhenChapterMissingAndNotInFlight()
    {
        var (context, chapterId, connector, settings) = SetupMissingChapter(nameof(DoWork_SchedulesDownload_WhenChapterMissingAndNotInFlight));

        var queue = new Mock<IWorkerQueue>();
        queue.Setup(q => q.GetKnownWorkers()).Returns([]);
        queue.Setup(q => q.GetRunningWorkers()).Returns([]);

        var worker = new StartNewChapterDownloadsWorker(settings, queue.Object, new[] { connector });

        BaseWorker[] created = await worker.DoWork(ScopeFor(context));

        Assert.Contains(created.OfType<DownloadChapterFromSourceWorker>(),
            w => w.ChapterIdId == chapterId.Key);
    }

    [Fact]
    public async Task DoWork_DoesNotScheduleDownload_ForBundledChapter()
    {
        var (context, chapterId, connector, settings) = SetupMissingChapter(nameof(DoWork_DoesNotScheduleDownload_ForBundledChapter));
        // The chapter's content lives inside a Vol N.cbz bundle (the individual file was deleted on
        // bundling). Even if CheckDownloaded flipped Downloaded=false because the loose file is gone,
        // it must NOT be re-downloaded — that recreates a duplicate beside the bundle. See bug A.
        var chapter = await context.Chapters.FirstAsync();
        chapter.IsBundled = true;
        chapter.Downloaded = false;
        await context.SaveChangesAsync();

        var queue = new Mock<IWorkerQueue>();
        queue.Setup(q => q.GetKnownWorkers()).Returns([]);
        queue.Setup(q => q.GetRunningWorkers()).Returns([]);

        var worker = new StartNewChapterDownloadsWorker(settings, queue.Object, new[] { connector });
        BaseWorker[] created = await worker.DoWork(ScopeFor(context));

        Assert.DoesNotContain(created.OfType<DownloadChapterFromSourceWorker>(),
            w => w.ChapterIdId == chapterId.Key);
    }

    [Fact]
    public async Task DoWork_DoesNotScheduleNegativeOrExcessWorkers_WhenInFlightExceedsLimit()
    {
        var (context, chapterId, connector, settings) = SetupMissingChapter(nameof(DoWork_DoesNotScheduleNegativeOrExcessWorkers_WhenInFlightExceedsLimit));
        settings.MaxConcurrentDownloads = 1;

        // More in-flight download workers than the limit. Slot calc must clamp at 0 (no throw, no new work).
        var w1 = new DownloadChapterFromSourceWorker(chapterId, new[] { connector }, settings);
        var w2 = new DownloadChapterFromSourceWorker(chapterId, new[] { connector }, settings);

        var queue = new Mock<IWorkerQueue>();
        queue.Setup(q => q.GetKnownWorkers()).Returns([w1, w2]);
        queue.Setup(q => q.GetRunningWorkers()).Returns([w1, w2]);

        var worker = new StartNewChapterDownloadsWorker(settings, queue.Object, new[] { connector });

        BaseWorker[] created = await worker.DoWork(ScopeFor(context));

        Assert.Empty(created);
    }
}
