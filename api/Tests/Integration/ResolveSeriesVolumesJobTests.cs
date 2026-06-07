using API.JobRuntime.Interfaces;
using API.JobRuntime.Reconcilers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

/// <summary>
/// Step-3: the ResolveSeriesVolumes handler runs on the real runtime. A resolve job enqueued through the
/// job store and run by the dispatcher resolves a series' chapter→volume assignments from the metadata
/// sources — the AF3 contract, now driven by the runtime instead of the legacy resolve worker.
/// </summary>
public class ResolveSeriesVolumesJobTests : OutboundHttpIntegrationTest
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Dandadan", name));

    private void StubRequiringUserAgent(Func<IRequestBuilder> request, string body)
    {
        Server.Given(request().WithHeader("User-Agent", new WildcardMatcher("*")))
            .AtPriority(1).RespondWith(Response.Create().WithStatusCode(200).WithBody(body));
        Server.Given(request())
            .AtPriority(2).RespondWith(Response.Create().WithStatusCode(400));
    }

    [Fact]
    public async Task EnqueuedResolveJob_ResolvesVolumes_ThroughTheRuntime()
    {
        StubRequiringUserAgent(() => Request.Create().WithPath("/manga").WithParam("title").UsingGet(), Fixture("search.json"));
        StubRequiringUserAgent(() => Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet(), Fixture("aggregate-all.json"));
        StubRequiringUserAgent(() => Request.Create().WithPath("/w/api.php").UsingGet(), Fixture("wikitext.json"));

        string seriesKey = await App.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-it-" + Guid.NewGuid().ToString("N")), "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Dandadan", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
            ctx.Series.Add(manga);
            foreach (var n in new[] { "1", "86", "201" })
                ctx.Chapters.Add(new Chapter(manga, n, null, null) { Downloaded = true, FileName = $"Ch.{n}.cbz" });
            ctx.Chapters.Add(new Chapter(manga, "2", null, null) { Downloaded = false });
            await ctx.SaveChangesAsync();
            return manga.Key;
        });

        using (var scope = App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                new JobEntity(ResolveSeriesVolumesHandler.Type, ResolveSeriesVolumesHandler.PayloadFor(seriesKey), DateTime.UtcNow));

        using (var scope = App.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync());

        var byNumber = await App.WithSeriesContext(c => c.Chapters.ToDictionaryAsync(x => x.ChapterNumber, x => x.VolumeNumber));
        Assert.Equal(1, byNumber["1"]);    // MangaDex
        Assert.Equal(1, byNumber["2"]);    // MangaDex — undownloaded, still resolved
        Assert.Equal(11, byNumber["86"]);  // beyond MangaDex → Wikipedia
        Assert.Equal(23, byNumber["201"]); // Wikipedia
    }
}
