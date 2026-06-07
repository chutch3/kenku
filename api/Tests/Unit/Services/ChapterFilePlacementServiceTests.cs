using API;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Unit.Services;

public class ChapterFilePlacementServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly SeriesContext _mangaContext;
    private const string NamingScheme = "?V(%M Vol %V/)%M - Ch.%C";

    public ChapterFilePlacementServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"PlaceChapterTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);

        var mangaOptions = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _mangaContext = new SeriesContext(mangaOptions);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, true);
        _mangaContext.Dispose();
    }

    private async Task<(Series manga, Chapter chapter)> SetupAsync(
        string chapterNumber = "1", int? volume = 5, string fileName = "One-Punch Man - Ch.1.cbz")
    {
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("One-Punch Man", "Desc", "url", SeriesReleaseStatus.Continuing,
            [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        var chapter = new Chapter(manga, chapterNumber, volume, null)
            { Downloaded = true, FileName = fileName };
        _mangaContext.Chapters.Add(chapter);
        await _mangaContext.SaveChangesAsync();
        return (manga, chapter);
    }

    private ChapterFilePlacementService CreateService() =>
        new(new KenkuSettings { AppData = _testRoot, ChapterNamingScheme = NamingScheme });

    [Fact]
    public async Task Place_WhenFileExistsAtOldPath_MovesFileAndUpdatesDatabaseFileName()
    {
        var (manga, chapter) = await SetupAsync();
        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        File.WriteAllText(Path.Combine(mangaDir, "One-Punch Man - Ch.1.cbz"), "fake content");

        const string newFileName = "One-Punch Man Vol 5/One-Punch Man - Ch.1.cbz";
        await CreateService().PlaceAsync(_mangaContext, chapter.Key, newFileName, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(mangaDir, "One-Punch Man - Ch.1.cbz")));
        Assert.True(File.Exists(Path.Combine(mangaDir, "One-Punch Man Vol 5", "One-Punch Man - Ch.1.cbz")));

        var updated = await _mangaContext.Chapters.FirstAsync(c => c.Key == chapter.Key);
        Assert.Equal(newFileName, updated.FileName);
    }

    [Fact]
    public async Task Place_WhenFileDoesNotExistAtOldPath_UpdatesDatabaseFilenameWithoutError()
    {
        var (_, chapter) = await SetupAsync();

        const string newFileName = "One-Punch Man Vol 5/One-Punch Man - Ch.1.cbz";
        await CreateService().PlaceAsync(_mangaContext, chapter.Key, newFileName, CancellationToken.None);

        var updated = await _mangaContext.Chapters.FirstAsync(c => c.Key == chapter.Key);
        Assert.Equal(newFileName, updated.FileName);
    }

    [Fact]
    public async Task Place_WhenDestinationAlreadyExists_DoesNotThrowAndUpdatesDatabaseFileName()
    {
        var (manga, chapter) = await SetupAsync(fileName: "One-Punch Man - Ch.1.cbz");
        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        File.WriteAllText(Path.Combine(mangaDir, "One-Punch Man - Ch.1.cbz"), "fake content");

        string destDir = Path.Combine(mangaDir, "One-Punch Man Vol 5");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "One-Punch Man - Ch.1.cbz"), "existing content");

        const string newFileName = "One-Punch Man Vol 5/One-Punch Man - Ch.1.cbz";
        var ex = await Record.ExceptionAsync(() =>
            CreateService().PlaceAsync(_mangaContext, chapter.Key, newFileName, CancellationToken.None));

        Assert.Null(ex);
        var updated = await _mangaContext.Chapters.FirstAsync(c => c.Key == chapter.Key);
        Assert.Equal(newFileName, updated.FileName);
    }

    [Fact]
    public async Task Place_WhenChapterKeyNotFound_CompletesWithoutError()
    {
        var ex = await Record.ExceptionAsync(() =>
            CreateService().PlaceAsync(_mangaContext, "nonexistent-key", "anything.cbz", CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Place_WhenAlreadyAtTarget_LeavesFileAlone()
    {
        var (manga, chapter) = await SetupAsync(fileName: "One-Punch Man Vol 5/One-Punch Man - Ch.1.cbz");
        string destDir = Path.Combine(_testRoot, manga.DirectoryName, "One-Punch Man Vol 5");
        Directory.CreateDirectory(destDir);
        File.WriteAllText(Path.Combine(destDir, "One-Punch Man - Ch.1.cbz"), "content");

        await CreateService().PlaceAsync(_mangaContext, chapter.Key, chapter.FileName, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(destDir, "One-Punch Man - Ch.1.cbz")));
    }

    [Fact]
    public async Task Place_WithNullTarget_ComputesLayoutCorrectName()
    {
        var (manga, chapter) = await SetupAsync();
        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        File.WriteAllText(Path.Combine(mangaDir, "One-Punch Man - Ch.1.cbz"), "fake content");

        await CreateService().PlaceAsync(_mangaContext, chapter.Key, null, CancellationToken.None);

        var updated = await _mangaContext.Chapters.FirstAsync(c => c.Key == chapter.Key);
        Assert.Equal("One-Punch Man Vol 5/One-Punch Man - Ch.1.cbz", updated.FileName);
    }
}
