using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// A true integration test: the real application is hosted in-process, the DI container builds every
/// object, and the test only touches the edges — an in-memory DB (seeded through the app's own context)
/// and a WireMock server the app's HttpClients are pointed at. Resolution is driven through the real HTTP
/// endpoint. The WireMock stubs answer 200 only when a User-Agent is present and 400 otherwise — mirroring
/// MangaDex/Wikipedia — so a regression that drops the User-Agent fails this test.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeResolutionEndToEndTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly KenkuApplicationFactory _app;

    public VolumeResolutionEndToEndTests() => _app = new KenkuApplicationFactory { OutboundHttpTarget = _server.Url! };

    public void Dispose() { _app.Dispose(); _server.Stop(); }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Dandadan", name));

    // The upstream answers 200 only with a User-Agent; without one it 400s, exactly like the real APIs.
    private void StubRequiringUserAgent(Func<IRequestBuilder> request, string body)
    {
        _server.Given(request().WithHeader("User-Agent", new WildcardMatcher("*")))
            .AtPriority(1).RespondWith(Response.Create().WithStatusCode(200).WithBody(body));
        _server.Given(request())
            .AtPriority(2).RespondWith(Response.Create().WithStatusCode(400));
    }

    private static async Task<bool> WaitUntil(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(100);
        }
        return false;
    }

    [Fact]
    public async Task ResolveMissingVolumesEndpoint_ResolvesFromMetadataSources_OverTheRealHttpStack()
    {
        StubRequiringUserAgent(() => Request.Create().WithPath("/manga").WithParam("title").UsingGet(), Fixture("search.json"));
        StubRequiringUserAgent(() => Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet(), Fixture("aggregate-all.json"));
        StubRequiringUserAgent(() => Request.Create().WithPath("/w/api.php").UsingGet(), Fixture("wikitext.json"));

        // Seed an unlinked series with unassigned chapters through the app's own DbContext.
        await _app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-it-" + Guid.NewGuid().ToString("N")), "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Dandadan", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
            ctx.Series.Add(manga);
            foreach (var n in new[] { "1", "86", "201" })
                ctx.Chapters.Add(new Chapter(manga, n, null, null) { Downloaded = true, FileName = $"Ch.{n}.cbz" });
            // An undownloaded chapter (no .cbz): exact sources map it by number, so it must still resolve.
            ctx.Chapters.Add(new Chapter(manga, "2", null, null) { Downloaded = false });
            await ctx.SaveChangesAsync();
            return 0;
        });

        // Drive it the way a client would.
        var response = await _app.CreateClient().PostAsync("/v2/Maintenance/ResolveMissingVolumes", null);
        response.EnsureSuccessStatusCode();

        // The real worker queue runs the coordinator → resolvers → WireMock in the background.
        bool resolved = await WaitUntil(
            async () => (await _app.WithSeriesContext(c => c.Chapters.CountAsync(x => x.VolumeNumber == null))) == 0,
            TimeSpan.FromSeconds(30));
        Assert.True(resolved, "chapters were not resolved over the real HTTP stack");

        var byNumber = await _app.WithSeriesContext(c => c.Chapters.ToDictionaryAsync(x => x.ChapterNumber, x => x.VolumeNumber));
        Assert.Equal(1, byNumber["1"]);     // MangaDex
        Assert.Equal(1, byNumber["2"]);     // MangaDex — undownloaded, still resolved
        Assert.Equal(11, byNumber["86"]);   // beyond MangaDex → Wikipedia
        Assert.Equal(23, byNumber["201"]);  // Wikipedia
    }
}
