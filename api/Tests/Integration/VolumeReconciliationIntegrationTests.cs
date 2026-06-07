using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Integration;

/// <summary>
/// Integration test for #23: when a chapter's volume is assigned AFTER it was downloaded (WeebCentral
/// hands chapters over with no volume; it's guessed later), its on-disk file keeps the old volume-less
/// name while the expected name now has a "Vol.N" prefix and (under VolumeFolder/VolumeCBZ) a "Vol N/"
/// folder. Nothing reconciles this, so CheckDownloaded flips the chapter not-downloaded and the workers
/// thrash. The reconciler must move the file to the layout-aware path and update FileName.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeReconciliationIntegrationTests
{
    [Fact]
    public async Task ChapterWithVolumeButStaleFlatName_IsMovedToVolumeFolder()
    {
        using var harness = new IntegrationHarness(); // default "%M - ?V(Vol.%V )Ch.%C?T( - %T)" scheme
        string chapterKey = null!;
        string mangaDir = null!;

        await harness.Seed(async ctx =>
        {
            var library = new FileLibrary(harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Dandadan", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing,
                [], [], [], [], library);
            manga.LibraryLayout = LibraryLayout.VolumeFolder;
            ctx.Series.Add(manga);

            mangaDir = Path.Combine(harness.TempDir, manga.DirectoryName);
            Directory.CreateDirectory(mangaDir);
            // Downloaded while volume was null (flat name on disk), then volume 1 was assigned later —
            // FileName still the old flat name.
            var chapter = new Chapter(manga, "1", 1, null) { Downloaded = true, FileName = "Dandadan - Ch.1.cbz" };
            ctx.Chapters.Add(chapter);
            chapterKey = chapter.Key;
            await File.WriteAllBytesAsync(Path.Combine(mangaDir, "Dandadan - Ch.1.cbz"), new byte[] { 1, 2, 3 });
        });

        // The reconciler enqueues a PlaceChapterFile job, which the harness then runs.
        await harness.ReconcileChapterFilePlacement();

        string expectedRelative = Path.Combine("Vol 1", "Dandadan - Vol.1 Ch.1.cbz");
        string expectedFull = Path.Combine(mangaDir, "Vol 1", "Dandadan - Vol.1 Ch.1.cbz");

        Assert.True(File.Exists(expectedFull), $"Chapter should be reconciled to {expectedFull}");
        Assert.False(File.Exists(Path.Combine(mangaDir, "Dandadan - Ch.1.cbz")), "Old loose file should be gone");

        string? fileName = await harness.Query(c =>
            c.Chapters.Where(x => x.Key == chapterKey).Select(x => x.FileName).FirstAsync());
        Assert.Equal(expectedRelative, fileName);
    }

    [Fact]
    public async Task AlreadyReconciledChapter_IsLeftAlone()
    {
        using var harness = new IntegrationHarness();
        await harness.Seed(async ctx =>
        {
            var library = new FileLibrary(harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("S", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            manga.LibraryLayout = LibraryLayout.VolumeFolder;
            ctx.Series.Add(manga);
            // Already at the layout-aware path — nothing to do.
            ctx.Chapters.Add(new Chapter(manga, "1", 1, null) { Downloaded = true, FileName = Path.Combine("Vol 1", "S - Vol.1 Ch.1.cbz") });
            await Task.CompletedTask;
        });

        var jobs = await harness.ReconcileChapterFilePlacement();

        Assert.Empty(jobs);
    }
}
