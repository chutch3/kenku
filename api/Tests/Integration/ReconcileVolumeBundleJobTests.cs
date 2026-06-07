using API.JobRuntime.Interfaces;
using API.JobRuntime.Reconcilers;
using System.IO.Compression;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

/// <summary>
/// Step-2: the ReconcileVolumeBundle handler runs on the real runtime. A reconcile job enqueued through
/// the job store and run by the dispatcher bundles a ready volume — the AF4 bundling contract, now driven
/// by the runtime instead of the legacy bundle worker.
/// </summary>
public class ReconcileVolumeBundleJobTests : OutboundHttpIntegrationTest
{
    private static byte[] FakeCbz(int pages)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 1; i <= pages; i++)
                using (zip.CreateEntry($"{i:D3}.jpg").Open()) { }
        return ms.ToArray();
    }

    [Fact]
    public async Task EnqueuedReconcileJob_BundlesAReadyVolume_ThroughTheRuntime()
    {
        string libDir = Path.Combine(Path.GetTempPath(), "kenku-it-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(libDir);

        var seeded = await App.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(libDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Test Series", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing,
                [], [], [], [], library) { LibraryLayout = LibraryLayout.VolumeCBZ };
            ctx.Series.Add(manga);

            string dir = manga.FullDirectoryPath;
            Directory.CreateDirectory(dir);
            for (int i = 1; i <= 2; i++)
            {
                ctx.Chapters.Add(new Chapter(manga, i.ToString(), 1, null) { Downloaded = true, FileName = $"ch{i}.cbz" });
                await File.WriteAllBytesAsync(Path.Combine(dir, $"ch{i}.cbz"), FakeCbz(3));
            }
            ctx.Chapters.Add(new Chapter(manga, "3", 2, null) { Downloaded = false }); // vol 2 → vol 1 closed
            await ctx.SaveChangesAsync();
            return (key: manga.Key, dir);
        });

        using (var scope = App.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            await store.EnqueueAsync(new JobEntity(ReconcileVolumeBundleHandler.Type,
                ReconcileVolumeBundleHandler.PayloadFor(seeded.key, 1), DateTime.UtcNow));
        }

        using (var scope = App.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync());

        Assert.True(File.Exists(Path.Combine(seeded.dir, "Vol 1.cbz")),
            "the reconcile job should have bundled volume 1 through the runtime");
    }
}
