using API;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Unit.Services;

public class DownloadStateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-dlstate-" + Guid.NewGuid().ToString("N"));
    private readonly SeriesContext _context;
    private readonly KenkuSettings _settings;

    public DownloadStateServiceTests()
    {
        Directory.CreateDirectory(_root);
        _settings = new KenkuSettings { AppData = _root, ChapterNamingScheme = "%M - Ch.%C" };
        _context = new SeriesContext(new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose()
    {
        _context.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private async Task<Series> SeedSeries()
    {
        var library = new FileLibrary(_root, "Lib");
        _context.FileLibraries.Add(library);
        var series = new Series("Saga", "d", "c", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _context.Series.Add(series);
        await _context.SaveChangesAsync();
        return series;
    }

    [Fact]
    public async Task VerifyAll_MarksChapterDownloaded_WhenFileIsOnDisk()
    {
        var series = await SeedSeries();
        var chapter = new Chapter(series, "1", null, null) { Downloaded = false };
        _context.Chapters.Add(chapter);
        await _context.SaveChangesAsync();

        string path = chapter.GetFullFilepath(_settings.ChapterNamingScheme)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "cbz");

        await new DownloadStateService().VerifyAllAsync(_context, _settings, CancellationToken.None);

        var updated = await _context.Chapters.FirstAsync(c => c.Key == chapter.Key);
        Assert.True(updated.Downloaded);
        Assert.False(string.IsNullOrEmpty(updated.FileName));
    }

    [Fact]
    public async Task VerifyAll_FlipsChapterNotDownloaded_WhenFileIsMissing()
    {
        var series = await SeedSeries();
        var chapter = new Chapter(series, "1", null, null) { Downloaded = true, FileName = "Saga - Ch.1.cbz" };
        _context.Chapters.Add(chapter);
        await _context.SaveChangesAsync();

        await new DownloadStateService().VerifyAllAsync(_context, _settings, CancellationToken.None);

        var updated = await _context.Chapters.FirstAsync(c => c.Key == chapter.Key);
        Assert.False(updated.Downloaded);
        Assert.Null(updated.FileName);
    }
}
