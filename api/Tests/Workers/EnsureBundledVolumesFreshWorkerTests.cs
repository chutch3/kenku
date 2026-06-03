using API;
using API.Schema.ActionsContext;
using API.Schema.SeriesContext;
using API.Workers;
using API.Workers.MaintenanceWorkers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace API.Tests.Workers;

public class EnsureBundledVolumesFreshWorkerTests : IDisposable
{
    private readonly SeriesContext _mangaContext;
    private readonly ActionsContext _actionsContext;
    private readonly Mock<IServiceScope> _scope;

    public EnsureBundledVolumesFreshWorkerTests()
    {
        _mangaContext = new SeriesContext(new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _actionsContext = new ActionsContext(new DbContextOptionsBuilder<ActionsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var sp = new Mock<IServiceProvider>();
        sp.Setup(x => x.GetService(typeof(SeriesContext))).Returns(_mangaContext);
        sp.Setup(x => x.GetService(typeof(ActionsContext))).Returns(_actionsContext);
        _scope = new Mock<IServiceScope>();
        _scope.Setup(x => x.ServiceProvider).Returns(sp.Object);
    }

    public void Dispose()
    {
        _mangaContext.Dispose();
        _actionsContext.Dispose();
    }

    // A bundled volume whose recorded membership is {ch1} but which now also owns a loose ch2.
    private async Task<(Series manga, VolumeMetadata vol)> SetupStaleVolumeAsync()
    {
        var library = new FileLibrary("/tmp", "Lib");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.LibraryLayout = LibraryLayout.VolumeCBZ;
        _mangaContext.Series.Add(manga);

        var vol = new VolumeMetadata(manga, 1) { ArchiveFileName = "Vol 1.cbz" };
        _mangaContext.VolumeMetadata.Add(vol);

        var ch1 = new Chapter(manga, "1", 1) { Downloaded = true, IsBundled = true };
        var ch2 = new Chapter(manga, "2", 1) { Downloaded = true, FileName = "ch2.cbz" };
        _mangaContext.Chapters.AddRange(ch1, ch2);
        _mangaContext.BundleChapterMaps.Add(new BundleChapterMap
            { VolumeKey = vol.Key, ChapterKey = ch1.Key, StartPage = 0, PageCount = 5 });

        await _mangaContext.SaveChangesAsync();
        return (manga, vol);
    }

    private EnsureBundledVolumesFreshWorker MakeWorker(params BaseWorker[] knownWorkers)
    {
        var queue = new Mock<IWorkerQueue>();
        queue.Setup(q => q.GetKnownWorkers()).Returns(knownWorkers);
        return new EnsureBundledVolumesFreshWorker(queue.Object, new KenkuSettings());
    }

    [Fact]
    public async Task StaleVolume_QueuesUnbundleThenRebundle()
    {
        var (manga, _) = await SetupStaleVolumeAsync();

        var jobs = await MakeWorker().DoWork(_scope.Object);

        var unbundle = Assert.Single(jobs.OfType<UnbundleVolumeWorker>());
        var rebundle = Assert.Single(jobs.OfType<BundleVolumeWorker>());
        Assert.Equal(manga.Key, unbundle.MangaId);
        Assert.Equal(1, unbundle.VolumeNumber);
        Assert.Equal(1, rebundle.VolumeNumber);
        // Rebundle must wait for the unbundle to finish.
        Assert.Contains(unbundle, rebundle.MissingDependencies);
    }

    [Fact]
    public async Task FreshVolume_QueuesNothing()
    {
        var (_, vol) = await SetupStaleVolumeAsync();
        // Record ch2 too, so the bundle's membership matches the volume's chapters.
        var ch2 = await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2");
        _mangaContext.BundleChapterMaps.Add(new BundleChapterMap
            { VolumeKey = vol.Key, ChapterKey = ch2.Key, StartPage = 5, PageCount = 5 });
        await _mangaContext.SaveChangesAsync();

        var jobs = await MakeWorker().DoWork(_scope.Object);

        Assert.Empty(jobs);
    }

    [Fact]
    public async Task InFlightRebuild_IsNotQueuedAgain()
    {
        var (manga, _) = await SetupStaleVolumeAsync();
        var alreadyRunning = new BundleVolumeWorker(manga.Key, 1, new KenkuSettings());

        var jobs = await MakeWorker(alreadyRunning).DoWork(_scope.Object);

        Assert.Empty(jobs);
    }
}
