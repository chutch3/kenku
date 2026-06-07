using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// #23, outside-in: a chapter downloaded before its volume was known keeps a flat, volume-less FileName;
/// once the volume is assigned the file must move into its layout folder. Driven through the booted app —
/// <c>POST /v2/Maintenance/SyncChapterFileNames</c> enqueues PlaceChapterFile jobs, the real dispatcher
/// runs them, and the file actually moves on disk.
/// </summary>
[Trait("Category", "Integration")]
public class ChapterPlacementEndToEndTests : OutboundHttpIntegrationTest
{
    private async Task<(string mangaDir, string chapterKey)> SeedStaleChapter(string fileName, bool fileOnDisk)
    {
        return await App.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-place-" + Guid.NewGuid().ToString("N")), "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Dandadan", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library)
                { LibraryLayout = LibraryLayout.VolumeFolder };
            ctx.Series.Add(manga);
            string mangaDir = manga.FullDirectoryPath;
            Directory.CreateDirectory(mangaDir);
            var chapter = new Chapter(manga, "1", 1, null) { Downloaded = true, FileName = fileName };
            ctx.Chapters.Add(chapter);
            if (fileOnDisk)
            {
                string full = Path.Combine(mangaDir, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllBytesAsync(full, [1, 2, 3]);
            }
            await ctx.SaveChangesAsync();
            return (mangaDir, chapter.Key);
        });
    }

    [Fact]
    public async Task ChapterWithStaleFlatName_IsMovedToVolumeFolder()
    {
        var (mangaDir, chapterKey) = await SeedStaleChapter("Dandadan - Ch.1.cbz", fileOnDisk: true);

        using var client = App.CreateClient();
        var response = await client.PostAsync("/v2/Maintenance/SyncChapterFileNames", null);
        response.EnsureSuccessStatusCode();
        await DrainJobsAsync();

        string expectedRelative = Path.Combine("Vol 1", "Dandadan - Vol.1 Ch.1.cbz");
        Assert.True(File.Exists(Path.Combine(mangaDir, expectedRelative)), "chapter should be reconciled into its volume folder");
        Assert.False(File.Exists(Path.Combine(mangaDir, "Dandadan - Ch.1.cbz")), "old loose file should be gone");

        string? fileName = await App.WithSeriesContext(c =>
            c.Chapters.Where(x => x.Key == chapterKey).Select(x => x.FileName).FirstAsync());
        Assert.Equal(expectedRelative, fileName);
    }

    [Fact]
    public async Task ChapterAlreadyAtLayoutPath_IsLeftAlone()
    {
        string correct = Path.Combine("Vol 1", "Dandadan - Vol.1 Ch.1.cbz");
        var (mangaDir, chapterKey) = await SeedStaleChapter(correct, fileOnDisk: true);

        using var client = App.CreateClient();
        (await client.PostAsync("/v2/Maintenance/SyncChapterFileNames", null)).EnsureSuccessStatusCode();
        await DrainJobsAsync();

        Assert.True(File.Exists(Path.Combine(mangaDir, correct)), "an already-correct file must stay put");
        string? fileName = await App.WithSeriesContext(c =>
            c.Chapters.Where(x => x.Key == chapterKey).Select(x => x.FileName).FirstAsync());
        Assert.Equal(correct, fileName);
    }
}
