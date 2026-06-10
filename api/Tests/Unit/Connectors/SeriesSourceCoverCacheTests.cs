using API.Tests;
using API.HttpRequesters.Interfaces;
using System.Net;
using API;
using API.Connectors;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace API.Tests.Unit.Connectors;

public class SeriesSourceCoverCacheTests
{
    /// <summary>Minimal connector whose download client is backed by a faked HTTP boundary.</summary>
    [Fact]
    public async Task SaveCoverImageToCache_ReturnedFilename_MatchesFileWrittenToCache()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "kenku-cover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var settings = new KenkuSettings { AppData = tempRoot };
            byte[] jpeg = TestImages.Jpeg();

            var inner = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(jpeg)
            });
            using var rateLimit = new RateLimitHandler(settings, inner);
            var downloadClient = new HttpRequester(rateLimit, settings);
            var connector = new FakeSeriesSource("Fake:Conn", settings, httpRequester: downloadClient);

            var library = new FileLibrary(Path.Combine(tempRoot, "lib"), "Lib");
            var manga = new Series("Cover Series", "Desc", "https://example.com/img/cover.png", SeriesReleaseStatus.Continuing,
                new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
                library, 0f, 2024, "en");
            // Connector name contains a Windows-forbidden character (':') so a cleaned-vs-uncleaned
            // mismatch in the returned filename is observable.
            var mcId = new SourceId<Series>(manga, "Fake:Conn", "site-id", "https://fake.com/x", true);

            string? returned = await connector.SaveCoverImageToCache(mcId);

            Assert.NotNull(returned);
            Assert.True(File.Exists(Path.Join(settings.CoverImageCacheOriginal, returned)),
                "The returned cover filename must correspond to the file actually written to the cache.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
