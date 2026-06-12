using API.Tests;
using API.Acquirers.Interfaces;
using API.DownloadClients.Interfaces;
using API.Indexers.Interfaces;
using API;
using API.Acquirers;
using API.Indexers;
using API.Connectors;
using API.Schema.SeriesContext;
using API.DownloadClients;
using Moq;
using Xunit;

namespace API.Tests.Unit.Acquirers;

public class TorrentAcquirerTests
{
    private static (SourceId<Chapter> chapterId, FakeSeriesSource source, string tempRoot) BuildFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "kenku-tor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var settings = new KenkuSettings { AppData = tempRoot };
        var library = new FileLibrary(Path.Combine(tempRoot, "lib"), "Lib");
        var series = new Series("Saga", "d", "c", SeriesReleaseStatus.Continuing,
            new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
            library, 0f, 2024, "en");
        var chapter = new Chapter(series, "60", null, null);
        var chapterId = new SourceId<Chapter>(chapter, "FakeTorrent", "60", null, true);
        return (chapterId, new FakeSeriesSource("FakeTorrent", settings, AcquisitionKind.Torrent), tempRoot);
    }

    [Fact]
    public async Task AcquireAsync_HandsOffSelectedReleaseToTorrentClient_AndDefers()
    {
        var (chapterId, source, tempRoot) = BuildFixture();
        try
        {
            var release = new IndexerSearchResult("Saga 060.cbz", "magnet:?xt=urn:btih:abc", 1000, 50, "ix");
            var indexer = new Mock<IIndexerClient>();
            indexer.Setup(i => i.Search(It.IsAny<IndexerQuery>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([release]);

            string? capturedUrl = null, capturedTag = null, capturedDir = null;
            var torrent = new Mock<IDownloadClient>();
            torrent.Setup(t => t.Add(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .Callback<string, string, string, CancellationToken>((u, d, tag, _) => { capturedUrl = u; capturedDir = d; capturedTag = tag; })
                   .ReturnsAsync("tag-result");

            var selector = new ReleaseSelector { MinSeeders = 1, PreferredTokens = ["cbz"], BlockedTokens = [] };
            var acquirer = new TorrentAcquirer(indexer.Object, torrent.Object, selector,
                new TorrentAcquirerSettings(Path.Combine(tempRoot, "staging"), [8000]));

            AcquireResult result = await acquirer.AcquireAsync(chapterId, source, Path.Combine(tempRoot, "x.cbz"), CancellationToken.None);

            // Deferred because the file isn't ready synchronously — the completion reconciler finishes it.
            Assert.IsType<AcquireResult.Deferred>(result);
            Assert.Equal("magnet:?xt=urn:btih:abc", capturedUrl);
            Assert.Equal(chapterId.Key, capturedTag);
            Assert.StartsWith(Path.Combine(tempRoot, "staging"), capturedDir!);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AcquireAsync_FallsBackToACoveringPack_WhenNoExactReleaseExists()
    {
        var (chapterId, source, tempRoot) = BuildFixture();
        try
        {
            var pack = new IndexerSearchResult("Saga 001-066 (digital)", "magnet:?xt=urn:btih:pack", 9000, 25, "ix");
            var indexer = new Mock<IIndexerClient>();
            indexer.Setup(i => i.Search(It.Is<IndexerQuery>(q => q.IssueNumber != null), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([]);
            indexer.Setup(i => i.Search(It.Is<IndexerQuery>(q => q.IssueNumber == null), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([pack]);

            string? capturedUrl = null, capturedTag = null;
            var torrent = new Mock<IDownloadClient>();
            torrent.Setup(t => t.Add(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .Callback<string, string, string, CancellationToken>((u, _, tag, _) => { capturedUrl = u; capturedTag = tag; })
                   .ReturnsAsync("tag-result");

            var selector = new ReleaseSelector { MinSeeders = 1, PreferredTokens = [], BlockedTokens = [] };
            var acquirer = new TorrentAcquirer(indexer.Object, torrent.Object, selector,
                new TorrentAcquirerSettings(Path.Combine(tempRoot, "staging"), [8000]));

            AcquireResult result = await acquirer.AcquireAsync(chapterId, source, Path.Combine(tempRoot, "x.cbz"), CancellationToken.None);

            Assert.IsType<AcquireResult.Deferred>(result);
            Assert.Equal("magnet:?xt=urn:btih:pack", capturedUrl);
            Assert.Equal(PackTag.For(chapterId.Obj.ParentManga.Key, "magnet:?xt=urn:btih:pack"), capturedTag);
        }
        finally { try { Directory.Delete(tempRoot, recursive: true); } catch { } }
    }

    [Fact]
    public async Task AcquireAsync_Defers_WithoutReAdding_WhenTheCoveringPackIsAlreadyInFlight()
    {
        var (chapterId, source, tempRoot) = BuildFixture();
        try
        {
            var pack = new IndexerSearchResult("Saga 001-066 (digital)", "magnet:?xt=urn:btih:pack", 9000, 25, "ix");
            var indexer = new Mock<IIndexerClient>();
            indexer.Setup(i => i.Search(It.Is<IndexerQuery>(q => q.IssueNumber != null), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([]);
            indexer.Setup(i => i.Search(It.Is<IndexerQuery>(q => q.IssueNumber == null), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([pack]);

            // Another chapter of the run already handed this pack off — same deterministic tag.
            var torrent = new Mock<IDownloadClient>();
            torrent.Setup(t => t.GetStatus(It.Is<string>(s => s.StartsWith("pack:")), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new DownloadStatus.Downloading(0.4));

            var selector = new ReleaseSelector { MinSeeders = 1, PreferredTokens = [], BlockedTokens = [] };
            var acquirer = new TorrentAcquirer(indexer.Object, torrent.Object, selector,
                new TorrentAcquirerSettings(Path.Combine(tempRoot, "staging"), [8000]));

            AcquireResult result = await acquirer.AcquireAsync(chapterId, source, Path.Combine(tempRoot, "x.cbz"), CancellationToken.None);

            Assert.IsType<AcquireResult.Deferred>(result);
            torrent.Verify(t => t.Add(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { try { Directory.Delete(tempRoot, recursive: true); } catch { } }
    }

    [Fact]
    public async Task AcquireAsync_Fails_WhenIndexerHasNoResults()
    {
        var (chapterId, source, tempRoot) = BuildFixture();
        try
        {
            var indexer = new Mock<IIndexerClient>();
            indexer.Setup(i => i.Search(It.IsAny<IndexerQuery>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([]);
            var torrent = new Mock<IDownloadClient>();
            var acquirer = new TorrentAcquirer(indexer.Object, torrent.Object, new ReleaseSelector(),
                new TorrentAcquirerSettings(Path.Combine(tempRoot, "staging"), [8000]));

            AcquireResult result = await acquirer.AcquireAsync(chapterId, source, Path.Combine(tempRoot, "x.cbz"), CancellationToken.None);

            Assert.Contains("no torrent releases found", Assert.IsType<AcquireResult.Failed>(result).Reason);
            torrent.Verify(t => t.Add(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { try { Directory.Delete(tempRoot, recursive: true); } catch { } }
    }

    [Fact]
    public async Task AcquireAsync_Fails_WhenSelectorRejectsAllReleases()
    {
        var (chapterId, source, tempRoot) = BuildFixture();
        try
        {
            var release = new IndexerSearchResult("Saga 060.cbr", "x", 1000, 1, "ix"); // .cbr blocked
            var indexer = new Mock<IIndexerClient>();
            indexer.Setup(i => i.Search(It.IsAny<IndexerQuery>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([release]);
            var torrent = new Mock<IDownloadClient>();
            var selector = new ReleaseSelector { MinSeeders = 1, PreferredTokens = [], BlockedTokens = ["cbr"] };
            var acquirer = new TorrentAcquirer(indexer.Object, torrent.Object, selector,
                new TorrentAcquirerSettings(Path.Combine(tempRoot, "staging"), [8000]));

            AcquireResult result = await acquirer.AcquireAsync(chapterId, source, Path.Combine(tempRoot, "x.cbz"), CancellationToken.None);

            Assert.Contains("no release passed selection", Assert.IsType<AcquireResult.Failed>(result).Reason);
            torrent.Verify(t => t.Add(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { try { Directory.Delete(tempRoot, recursive: true); } catch { } }
    }

    [Fact]
    public async Task AcquireAsync_Defers_WithoutSearchingOrReAdding_WhenTorrentAlreadyInClient()
    {
        var (chapterId, source, tempRoot) = BuildFixture();
        try
        {
            var indexer = new Mock<IIndexerClient>();
            var torrent = new Mock<IDownloadClient>();
            torrent.Setup(t => t.GetStatus(chapterId.Key, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new DownloadStatus.Downloading(0.4));
            var acquirer = new TorrentAcquirer(indexer.Object, torrent.Object, new ReleaseSelector(),
                new TorrentAcquirerSettings(Path.Combine(tempRoot, "staging"), [8000]));

            AcquireResult result = await acquirer.AcquireAsync(chapterId, source, Path.Combine(tempRoot, "x.cbz"), CancellationToken.None);

            Assert.IsType<AcquireResult.Deferred>(result);
            indexer.Verify(i => i.Search(It.IsAny<IndexerQuery>(), It.IsAny<CancellationToken>()), Times.Never);
            torrent.Verify(t => t.Add(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { try { Directory.Delete(tempRoot, recursive: true); } catch { } }
    }

    [Fact]
    public async Task AcquireAsync_Fails_WhenTorrentErroredInClient()
    {
        var (chapterId, source, tempRoot) = BuildFixture();
        try
        {
            var indexer = new Mock<IIndexerClient>();
            var torrent = new Mock<IDownloadClient>();
            torrent.Setup(t => t.GetStatus(chapterId.Key, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new DownloadStatus.Errored("disk full"));
            var acquirer = new TorrentAcquirer(indexer.Object, torrent.Object, new ReleaseSelector(),
                new TorrentAcquirerSettings(Path.Combine(tempRoot, "staging"), [8000]));

            AcquireResult result = await acquirer.AcquireAsync(chapterId, source, Path.Combine(tempRoot, "x.cbz"), CancellationToken.None);

            Assert.Contains("disk full", Assert.IsType<AcquireResult.Failed>(result).Reason);
            torrent.Verify(t => t.Add(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { try { Directory.Delete(tempRoot, recursive: true); } catch { } }
    }
}
