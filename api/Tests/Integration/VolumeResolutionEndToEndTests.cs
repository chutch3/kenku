using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// A true integration test: the real application is hosted in-process, the DI container builds every
/// object, and the test only touches the edges — an in-memory DB and a WireMock server. Resolution is
/// driven through the real HTTP endpoint. The WireMock stubs answer 200 only when a User-Agent is present
/// and 400 otherwise — mirroring MangaDex/Wikipedia — so a regression that drops the User-Agent fails this.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeResolutionEndToEndTests : OutboundHttpIntegrationTest
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Dandadan", name));

    // The upstream answers 200 only with a User-Agent; without one it 400s, exactly like the real APIs.
    private void StubRequiringUserAgent(Func<IRequestBuilder> request, string body)
    {
        Server.Given(request().WithHeader("User-Agent", new WildcardMatcher("*")))
            .AtPriority(1).RespondWith(Response.Create().WithStatusCode(200).WithBody(body));
        Server.Given(request())
            .AtPriority(2).RespondWith(Response.Create().WithStatusCode(400));
    }

    [Fact]
    public async Task ResolveMissingVolumesEndpoint_ResolvesFromMetadataSources_OverTheRealHttpStack()
    {
        StubRequiringUserAgent(() => Request.Create().WithPath("/manga").WithParam("title").UsingGet(), Fixture("search.json"));
        StubRequiringUserAgent(() => Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet(), Fixture("aggregate-all.json"));
        StubRequiringUserAgent(() => Request.Create().WithPath("/w/api.php").UsingGet(), Fixture("wikitext.json"));

        // Seed an unlinked series with unassigned chapters through the app's own DbContext.
        await App.WithSeriesContext(async ctx =>
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

        var response = await App.CreateClient().PostAsync("/v2/Maintenance/ResolveMissingVolumes", null);
        response.EnsureSuccessStatusCode();
        await DrainJobsAsync();

        int unresolved = await App.WithSeriesContext(c => c.Chapters.CountAsync(x => x.VolumeNumber == null));
        Assert.Equal(0, unresolved);

        var byNumber = await App.WithSeriesContext(c => c.Chapters.ToDictionaryAsync(x => x.ChapterNumber, x => x.VolumeNumber));
        Assert.Equal(1, byNumber["1"]);     // MangaDex
        Assert.Equal(1, byNumber["2"]);     // MangaDex — undownloaded, still resolved
        Assert.Equal(11, byNumber["86"]);   // beyond MangaDex → Wikipedia
        Assert.Equal(23, byNumber["201"]);  // Wikipedia
    }
}
