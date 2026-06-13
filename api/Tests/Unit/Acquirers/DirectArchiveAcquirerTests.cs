using API.Tests;
using System.Net;
using API;
using API.Acquirers.Interfaces;
using API.Acquirers;
using API.Connectors;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using Xunit;

namespace API.Tests.Unit.Acquirers;

public class DirectArchiveAcquirerTests
{
    private static (Series series, Chapter chapter, SourceId<Chapter> sourceId, FakeSeriesSource source, KenkuSettings settings, string tempRoot)
        BuildFixture(string archiveUrl)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "kenku-da-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var settings = new KenkuSettings { AppData = tempRoot };
        var library = new FileLibrary(Path.Combine(tempRoot, "lib"), "Lib");
        var series = new Series("S", "d", "https://x/c.png", SeriesReleaseStatus.Continuing,
            new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
            library, 0f, 2024, "en");
        var chapter = new Chapter(series, "1", null, "T");
        var sourceId = new SourceId<Chapter>(chapter, "FakeArchive", "s1", archiveUrl, true);
        var source = new FakeSeriesSource("FakeArchive", settings, AcquisitionKind.DirectArchive);
        return (series, chapter, sourceId, source, settings, tempRoot);
    }

    private sealed class ChoiceResolvingSource(KenkuSettings settings, ArchiveResolution resolution)
        : SeriesSource("FakeArchive", ["en"], ["fake.test"], "icon", settings), IArchiveUrlResolver
    {
        public override AcquisitionKind Kind => AcquisitionKind.DirectArchive;
        public Task<ArchiveResolution> ResolveArchiveUrl(SourceId<Chapter> chapter, CancellationToken ct) =>
            Task.FromResult(resolution);
        public override Task<(Series, SourceId<Series>)[]> SearchManga(string m) => throw new NotSupportedException();
        public override Task<(Series, SourceId<Series>)?> GetMangaFromUrl(string url) => throw new NotSupportedException();
        public override Task<(Series, SourceId<Series>)?> GetMangaFromId(string id) => throw new NotSupportedException();
        public override Task<(Chapter, SourceId<Chapter>)[]> GetChapters(SourceId<Series> id, string? language = null) => throw new NotSupportedException();
        internal override Task<string[]> GetChapterImageUrls(SourceId<Chapter> id) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AcquireAsync_Fails_WithAnActionableMessage_WhenResolutionOffersChoices()
    {
        var (_, _, sourceId, _, settings, tempRoot) = BuildFixture("https://getcomics.org/c/spawn-376/");
        try
        {
            var source = new ChoiceResolvingSource(settings, new ArchiveResolution.Choice(
            [
                new DownloadOption("Spawn #376 (Empire)", "https://getcomics.org/dls/empire", "89 MB"),
                new DownloadOption("Spawn #376", "https://getcomics.org/dls/series", "67 MB"),
            ]));
            using var http = new HttpClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not download")));
            var acquirer = new DirectArchiveAcquirer(http);

            AcquireResult result = await acquirer.AcquireAsync(sourceId, source, Path.Combine(tempRoot, "out.cbz"), CancellationToken.None);

            var failed = Assert.IsType<AcquireResult.Failed>(result);
            Assert.Contains("2 downloads", failed.Reason);
            Assert.Contains("choose", failed.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(tempRoot, recursive: true); } catch { } }
    }

    [Fact]
    public async Task AcquireAsync_DownloadsThePinnedUrl_WithoutResolving()
    {
        byte[] archiveBytes = [0x50, 0x4B, 0x03, 0x04];
        var (_, _, sourceId, _, settings, tempRoot) = BuildFixture("https://getcomics.org/c/spawn-376/");
        try
        {
            // A resolver that would park the chapter — the pin must bypass it entirely.
            var source = new ChoiceResolvingSource(settings, new ArchiveResolution.Manual("nope"));
            string saveTo = Path.Combine(tempRoot, "out.cbz");
            string capturedUrl = "";
            using var http = new HttpClient(new FakeHttpMessageHandler(req =>
            {
                capturedUrl = req.RequestUri!.ToString();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archiveBytes) };
            }));
            var acquirer = new DirectArchiveAcquirer(http);

            AcquireResult result = await acquirer.AcquireAsync(sourceId, source, saveTo, CancellationToken.None,
                pinnedArchiveUrl: "https://getcomics.org/dls/series");

            Assert.Equal(saveTo, Assert.IsType<AcquireResult.Acquired>(result).Path);
            Assert.Equal("https://getcomics.org/dls/series", capturedUrl);
        }
        finally { try { Directory.Delete(tempRoot, recursive: true); } catch { } }
    }

    [Fact]
    public async Task AcquireAsync_DownloadsArchiveFromChapterWebsiteUrl_AndReturnsPath()
    {
        const string archiveUrl = "https://fake.test/series/x/chapter1.cbz";
        byte[] archiveBytes = [0x50, 0x4B, 0x03, 0x04, 0xAA, 0xBB]; // ZIP magic + a couple bytes
        var (_, _, sourceId, source, settings, tempRoot) = BuildFixture(archiveUrl);
        try
        {
            string saveTo = Path.Combine(tempRoot, "out.cbz");
            string capturedUrl = "";
            var inner = new FakeHttpMessageHandler(req =>
            {
                capturedUrl = req.RequestUri!.ToString();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archiveBytes)
                };
            });
            using var http = new HttpClient(inner);
            var acquirer = new DirectArchiveAcquirer(http);

            AcquireResult result = await acquirer.AcquireAsync(sourceId, source, saveTo, CancellationToken.None);

            Assert.Equal(saveTo, Assert.IsType<AcquireResult.Acquired>(result).Path);
            Assert.Equal(archiveUrl, capturedUrl);
            Assert.True(File.Exists(saveTo), "Acquirer must write the archive to disk.");
            Assert.Equal(archiveBytes, await File.ReadAllBytesAsync(saveTo));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task AcquireAsync_Fails_WhenServerReturnsNonSuccess()
    {
        var (_, _, sourceId, source, settings, tempRoot) = BuildFixture("https://fake.test/missing.cbz");
        try
        {
            var inner = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            using var http = new HttpClient(inner);
            var acquirer = new DirectArchiveAcquirer(http);
            string saveTo = Path.Combine(tempRoot, "out.cbz");

            AcquireResult result = await acquirer.AcquireAsync(sourceId, source, saveTo, CancellationToken.None);

            Assert.IsType<AcquireResult.Failed>(result);
            Assert.False(File.Exists(saveTo), "No file should be written on failure.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    /// <summary>A stand-in for a connector (like GetComics) whose chapters carry a post-page URL that
    /// must be resolved to the actual archive URL at download time.</summary>
    private sealed class ResolvingSource(string name, KenkuSettings settings, ArchiveResolution resolution)
        : SeriesSource(name, ["en"], ["fake.test"], "icon", settings), IArchiveUrlResolver
    {
        public override AcquisitionKind Kind => AcquisitionKind.DirectArchive;
        public Task<ArchiveResolution> ResolveArchiveUrl(SourceId<Chapter> chapter, CancellationToken ct) =>
            Task.FromResult(resolution);
        public override Task<(Series, SourceId<Series>)[]> SearchManga(string mangaSearchName) => throw new NotSupportedException();
        public override Task<(Series, SourceId<Series>)?> GetMangaFromUrl(string url) => throw new NotSupportedException();
        public override Task<(Series, SourceId<Series>)?> GetMangaFromId(string mangaIdOnSite) => throw new NotSupportedException();
        public override Task<(Chapter, SourceId<Chapter>)[]> GetChapters(SourceId<Series> mangaId, string? language = null) => throw new NotSupportedException();
        internal override Task<string[]> GetChapterImageUrls(SourceId<Chapter> chapterId) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AcquireAsync_DownloadsTheResolvedUrl_WhenTheSourceResolvesArchiveUrls()
    {
        // The chapter's WebsiteUrl is a post page, not the archive; the source's resolver supplies
        // the real archive URL at download time and that is what must be fetched.
        const string resolvedUrl = "https://fake.test/dls/abc123";
        var (_, _, sourceId, _, settings, tempRoot) = BuildFixture("https://fake.test/post/saga-60/");
        var source = new ResolvingSource("FakeArchive", settings, new ArchiveResolution.Resolved(resolvedUrl));
        try
        {
            string saveTo = Path.Combine(tempRoot, "out.cbz");
            string capturedUrl = "";
            var inner = new FakeHttpMessageHandler(req =>
            {
                capturedUrl = req.RequestUri!.ToString();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x50, 0x4B]) };
            });
            using var http = new HttpClient(inner);
            var acquirer = new DirectArchiveAcquirer(http);

            AcquireResult result = await acquirer.AcquireAsync(sourceId, source, saveTo, CancellationToken.None);

            Assert.IsType<AcquireResult.Acquired>(result);
            Assert.Equal(resolvedUrl, capturedUrl);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AcquireAsync_FailsWithTheManualReason_WithoutDownloading()
    {
        // A mirror-only post can't be automated; the resolver's reason must surface on the failed
        // job (→ NeedsAttention), and nothing must be fetched.
        const string reason = "only available via TERABOX, MEGA — download manually";
        var (_, _, sourceId, _, settings, tempRoot) = BuildFixture("https://fake.test/post/compendium/");
        var source = new ResolvingSource("FakeArchive", settings, new ArchiveResolution.Manual(reason));
        try
        {
            var http = new HttpClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not be called")));
            var acquirer = new DirectArchiveAcquirer(http);

            AcquireResult result = await acquirer.AcquireAsync(sourceId, source, Path.Combine(tempRoot, "x.cbz"), CancellationToken.None);

            Assert.Equal(reason, Assert.IsType<AcquireResult.Failed>(result).Reason);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AcquireAsync_Fails_WhenChapterHasNoWebsiteUrl()
    {
        var (_, _, _, source, settings, tempRoot) = BuildFixture("");
        try
        {
            // sourceId.WebsiteUrl is null/empty
            var chapter = new Chapter(new Series("S2", "d", "c", SeriesReleaseStatus.Continuing,
                new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
                new FileLibrary("/tmp", "L"), 0f, 2024, "en"), "1", null, null);
            var emptyId = new SourceId<Chapter>(chapter, "FakeArchive", "s1", null, true);

            var http = new HttpClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not be called")));
            var acquirer = new DirectArchiveAcquirer(http);

            AcquireResult result = await acquirer.AcquireAsync(emptyId, source, Path.Combine(tempRoot, "x.cbz"), CancellationToken.None);

            Assert.IsType<AcquireResult.Failed>(result);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }
}
