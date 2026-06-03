using API;
using API.Controllers;
using API.Schema.ActionsContext;
using API.Schema.SeriesContext;
using API.Services;
using API.Tests.Integration;
using API.Workers;
using API.Workers.MaintenanceWorkers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace API.Tests.Controllers;

/// <summary>
/// The reset/resolve maintenance flow end-to-end: the controller, the real <see cref="WorkerQueue"/>,
/// the resolver and the coordinator all run for real; only the MangaDex HTTP call is simulated (WireMock,
/// one aggregate mapping chapters 1 &amp; 2 to volume 1). Series are seeded NoMatch so auto-match is skipped.
/// </summary>
[Trait("Category", "Integration")]
public class MaintenanceControllerIntegrationTests : IDisposable
{
    private readonly IntegrationHarness _harness = new("?V(%M Vol %V/)%M - Ch.%C");
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() { _server.Stop(); _harness.Dispose(); }

    private static async Task<bool> WaitUntil(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(50);
        }
        return false;
    }

    private ResolveMissingVolumesForMangaWorkerFactory RealFactory()
    {
        _server
            .Given(Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(
                """{ "volumes": { "1": { "volume": "1", "chapters": { "1": { "chapter": "1" }, "2": { "chapter": "2" } } } } }"""));
        var http = new HttpClient(new HostRewritingHandler(_server.Url!));
        return new ResolveMissingVolumesForMangaWorkerFactory(
            _harness.Settings, new MangaDexVolumeResolver(http), new MangaDexSearchService(http), []);
    }

    [Fact]
    public async Task WithFewerWorkersThanSeries_AllSeriesStillGetResolved()
    {
        _harness.Settings.VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly;
        _harness.Settings.VolumeResolutionParallelism = 2;

        await _harness.Seed(ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            for (int i = 1; i <= 3; i++)
            {
                var manga = new Series($"Series {i}", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
                manga.MetadataSource!.Status = MetadataSourceStatus.NoMatch;
                manga.SourceIds.Add(new SourceId<Series>(manga, "MangaDex", $"uuid-{i}", null));
                ctx.Series.Add(manga);
                ctx.Chapters.Add(new Chapter(manga, "1", null, null) { Downloaded = true, FileName = $"manga{i}_ch1.cbz" });
            }
            return Task.CompletedTask;
        });

        var ran = await _harness.Run(new ResolveMissingVolumesWorker(_harness.Settings, RealFactory()));

        // min(parallelism=2, series=3) = 2 pool workers share one queue and drain all 3.
        Assert.Equal(2, ran.OfType<ResolveMissingVolumesForMangaWorker>().Count());
        var chapters = await _harness.Query(c => c.Chapters.ToListAsync());
        Assert.Equal(3, chapters.Count);
        Assert.All(chapters, c => Assert.Equal(1, c.VolumeNumber));
    }

    [Fact]
    public async Task ResetAndResolve_ClearsExistingVolumes_ThenReResolvesThem()
    {
        _harness.Settings.VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly;

        await _harness.Seed(ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("One-Punch Man", "Superhero comedy", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            manga.MetadataSource!.Status = MetadataSourceStatus.NoMatch;
            manga.SourceIds.Add(new SourceId<Series>(manga, "MangaDex", "some-uuid", null));
            ctx.Series.Add(manga);
            // Wrong volumes (5) from a previous buggy run.
            ctx.Chapters.Add(new Chapter(manga, "1", 5, null) { Downloaded = true, FileName = "One-Punch Man - Ch.1.cbz" });
            ctx.Chapters.Add(new Chapter(manga, "2", 5, null) { Downloaded = true, FileName = "One-Punch Man - Ch.2.cbz" });
            return Task.CompletedTask;
        });

        var queue = new WorkerQueue(_harness.Services, _harness.Settings);
        using (var scope = _harness.CreateScope())
        {
            var controller = new MaintenanceController(
                scope.ServiceProvider.GetRequiredService<SeriesContext>(),
                scope.ServiceProvider.GetRequiredService<ActionsContext>())
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            await controller.ResetAndResolveVolumes(queue, _harness.Settings, RealFactory());
        }

        // The endpoint cleared the volumes and queued resolution; the real queue resolves both back to 1.
        Assert.True(await WaitUntil(
            () => _harness.Query(async c => await c.Chapters.AllAsync(ch => ch.VolumeNumber == 1) && await c.Chapters.CountAsync() == 2),
            TimeSpan.FromSeconds(20)), "chapters were not re-resolved to volume 1");
    }
}
