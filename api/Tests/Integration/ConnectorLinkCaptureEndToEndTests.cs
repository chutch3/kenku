using System.Net;
using System.Text;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Outside-in: the real application is hosted in-process and a series is imported through the actual
/// add-to-library HTTP endpoint, so the request flows controller -> WeebCentral connector -> UpsertManga
/// -> EF. The only thing stubbed is the connector's HTTP edge (<see cref="IHttpRequester"/>), which is
/// now injected rather than constructed in the connector. The test asserts the external links the
/// connector surfaced actually round-tripped to the database.
/// </summary>
[Trait("Category", "Integration")]
public class ConnectorLinkCaptureEndToEndTests : IDisposable
{
    private readonly KenkuApplicationFactory _app;

    public ConnectorLinkCaptureEndToEndTests()
    {
        const string seriesPageHtml = """
            <html><head><title>Fire Punch | Weeb Central</title></head>
            <body><a href="https://anilist.co/manga/87170">AniList</a></body></html>
            """;

        var requester = new Mock<IHttpRequester>();
        requester
            .Setup(c => c.MakeRequest(It.IsAny<string>(), It.IsAny<RequestType>(), It.IsAny<string>(), It.IsAny<CancellationToken?>()))
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(seriesPageHtml, Encoding.UTF8, "text/html")
            });

        _app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = "http://localhost:9",
            ConnectorHttpRequester = requester.Object
        };
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task ImportingWeebCentralSeries_PersistsItsExternalLinks_OverTheRealStack()
    {
        string libraryKey = await _app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-conn-" + Guid.NewGuid().ToString("N")), "Lib");
            ctx.FileLibraries.Add(library);
            await ctx.SaveChangesAsync();
            return library.Key;
        });

        // Import the series the way a client does — the controller fetches it from the connector and persists it.
        var response = await _app.CreateClient().PostAsync(
            $"/v2/Series/unknown-id/ChangeLibrary/{libraryKey}?connectorName=WeebCentral&connectorSeriesId=wc-1", null);
        response.EnsureSuccessStatusCode();

        var links = await _app.WithSeriesContext(c =>
            c.Series.Include(m => m.Links)
                .Where(m => m.Name == "Fire Punch")
                .SelectMany(m => m.Links)
                .Select(l => l.LinkUrl)
                .ToListAsync());

        Assert.Contains("https://anilist.co/manga/87170", links);
    }
}
