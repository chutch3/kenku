using API.Schema.SeriesContext;
using API.Tests.Integration;
using API.Workers;
using API.Workers.MaintenanceWorkers;
using Moq;
using Xunit;

namespace API.Tests.Workers;

/// <summary>
/// Unit-level coverage of the level-triggered bundler's dedup decision: given a bundle already in flight,
/// it must not queue a duplicate. The work queue is mocked only to set the "in-flight" precondition.
/// </summary>
public class EnsureReadyVolumesBundledWorkerTests : IDisposable
{
    private readonly IntegrationHarness _harness = new("%M - Ch.%C");

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task DoesNotQueue_AVolumeAlreadyInFlight()
    {
        string mangaKey = null!;
        await _harness.Seed(ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("S", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library)
                { LibraryLayout = LibraryLayout.VolumeCBZ };
            ctx.Series.Add(manga);
            mangaKey = manga.Key;
            ctx.Chapters.Add(new Chapter(manga, "1", 1, null) { Downloaded = true, FileName = "ch1.cbz" });
            ctx.Chapters.Add(new Chapter(manga, "2", 2, null) { Downloaded = false });
            return Task.CompletedTask;
        });

        var queue = new Mock<IWorkerQueue>();
        queue.Setup(q => q.GetKnownWorkers()).Returns([new BundleVolumeWorker(mangaKey, 1, _harness.Settings)]);

        var spawned = await new EnsureReadyVolumesBundledWorker(queue.Object, _harness.Settings).DoWork(_harness.CreateScope());

        Assert.DoesNotContain(spawned.OfType<BundleVolumeWorker>(), b => b.MangaId == mangaKey && b.VolumeNumber == 1);
    }
}
