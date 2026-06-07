using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// A chapter downloaded before its volume was known has a FileName with no volume prefix. Once the
/// volume is known, the placement reconciler (and the jobs it enqueues) must move the file into the
/// volume subdirectory and update the DB. Runs the real reconcile→place path in fresh scopes via the harness.
/// </summary>
[Trait("Category", "Integration")]
public class ChapterFilePlacementIntegrationTests : IDisposable
{
    private readonly IntegrationHarness _harness = new("?V(%M Vol %V/)%M - Ch.%C");

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ChapterWithStaleFileName_IsMovedToItsVolumeSubdirectory()
    {
        string mangaDir = null!;
        await _harness.Seed(ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("One-Punch Man", "Superhero comedy", "url",
                SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(manga);
            mangaDir = manga.FullDirectoryPath;
            Directory.CreateDirectory(mangaDir);
            ctx.Chapters.Add(new Chapter(manga, "1", 5, null)
                { Downloaded = true, FileName = "One-Punch Man - Ch.1.cbz" });
            File.WriteAllText(Path.Combine(mangaDir, "One-Punch Man - Ch.1.cbz"), "fake cbz content");
            return Task.CompletedTask;
        });

        await _harness.ReconcileChapterFilePlacement();

        var result = await _harness.Query(c => c.Chapters.FirstAsync(x => x.ChapterNumber == "1"));
        Assert.Equal("One-Punch Man Vol 5/One-Punch Man - Ch.1.cbz", result.FileName);
        Assert.False(File.Exists(Path.Combine(mangaDir, "One-Punch Man - Ch.1.cbz")));
        Assert.True(File.Exists(Path.Combine(mangaDir, "One-Punch Man Vol 5", "One-Punch Man - Ch.1.cbz")));
    }
}
