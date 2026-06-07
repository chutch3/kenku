using API.Schema.ActionsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace API.Tests.Unit.Services;

public class CleanupServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly SeriesContext _mangaContext;
    private readonly ActionsContext _actionsContext;

    public CleanupServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"CleanupTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);

        var mangaOptions = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _mangaContext = new SeriesContext(mangaOptions);

        var actionsOptions = new DbContextOptionsBuilder<ActionsContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _actionsContext = new ActionsContext(actionsOptions);

    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, true);
        _mangaContext.Dispose();
        _actionsContext.Dispose();
    }

    [Fact]
    public async Task DoWork_RemovesOrphanedCbzFile()
    {
        // Setup library
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        
        // Setup tracked manga and chapter
        var manga = new Series("Tracked Series", "Desc", "http://example.com/cover.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        
        var chapter = new Chapter(manga, "1", 1) { Downloaded = true, FileName = "tracked.cbz" };
        _mangaContext.Chapters.Add(chapter);
        await _mangaContext.SaveChangesAsync();

        // Create files on disk
        string trackedFile = Path.Combine(_testRoot, manga.DirectoryName, "tracked.cbz");
        Directory.CreateDirectory(Path.GetDirectoryName(trackedFile)!);
        File.WriteAllText(trackedFile, "tracked content");

        string orphanedFile = Path.Combine(_testRoot, "orphaned.cbz");
        File.WriteAllText(orphanedFile, "orphaned content");

        // Run worker
        await new CleanupService().CleanupOrphanedFilesAsync(_mangaContext, dryRun: false, force: false, CancellationToken.None);

        Assert.True(File.Exists(trackedFile), "Tracked file should still exist");
        Assert.False(File.Exists(orphanedFile), "Orphaned file should be deleted");
    }

    [Fact]
    public async Task DoWork_WhenFileMovedToSubdirectory_RemovesOrphanedOriginalFile()
    {
        // Setup library
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        
        // Setup tracked manga and chapter pointing to a subdirectory
        var manga = new Series("MoveManga", "Desc", "http://example.com/cover.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        
        string subDir = "Volume 1";
        string fileName = "chapter1.cbz";
        var chapter = new Chapter(manga, "1", 1) { Downloaded = true, FileName = Path.Combine(subDir, fileName) };
        _mangaContext.Chapters.Add(chapter);
        await _mangaContext.SaveChangesAsync();

        // Create the tracked file in the subdirectory
        string trackedFile = Path.Combine(_testRoot, manga.DirectoryName, subDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(trackedFile)!);
        File.WriteAllText(trackedFile, "new content");

        // Create the orphaned original file in the root manga directory
        string orphanedFile = Path.Combine(_testRoot, manga.DirectoryName, fileName);
        File.WriteAllText(orphanedFile, "old content");

        // Run worker
        await new CleanupService().CleanupOrphanedFilesAsync(_mangaContext, dryRun: false, force: false, CancellationToken.None);

        Assert.True(File.Exists(trackedFile), "Tracked file in subdirectory should still exist");
        Assert.False(File.Exists(orphanedFile), "Orphaned original file in root directory should be deleted");
    }

    [Fact]
    public async Task DoWork_WhenLibraryHasFilesButNothingTracked_SkipsDeletion()
    {
        // A library full of archives with ZERO tracked chapters is the classic "library path wrong /
        // series not imported / DB reset" case — the worker must refuse to wipe it without force.
        var library = new FileLibrary(_testRoot, "Untracked Library");
        _mangaContext.FileLibraries.Add(library);
        await _mangaContext.SaveChangesAsync(); // deliberately no chapters tracked

        string a = Path.Combine(_testRoot, "a.cbz");
        string b = Path.Combine(_testRoot, "b.cbz");
        string c = Path.Combine(_testRoot, "c.cbz");
        File.WriteAllText(a, "1");
        File.WriteAllText(b, "2");
        File.WriteAllText(c, "3");

        await new CleanupService().CleanupOrphanedFilesAsync(_mangaContext, dryRun: false, force: false, CancellationToken.None);

        Assert.True(File.Exists(a) && File.Exists(b) && File.Exists(c),
            "An untracked library must not be wiped without force.");
    }

    [Fact]
    public async Task DoWork_WhenForced_DeletesUntrackedLibraryFiles()
    {
        var library = new FileLibrary(_testRoot, "Untracked Library");
        _mangaContext.FileLibraries.Add(library);
        await _mangaContext.SaveChangesAsync();

        string orphan = Path.Combine(_testRoot, "a.cbz");
        File.WriteAllText(orphan, "1");

        await new CleanupService().CleanupOrphanedFilesAsync(_mangaContext, dryRun: false, force: true, CancellationToken.None);

        Assert.False(File.Exists(orphan), "force=true should override the guard and delete orphans.");
    }


}
