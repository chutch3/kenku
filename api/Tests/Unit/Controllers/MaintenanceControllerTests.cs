using API.Controllers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Services;
using API.Schema.ActionsContext;
using API.Schema.SeriesContext;
using API.MetadataResolvers;
using API.MetadataResolvers.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace API.Tests.Unit.Controllers;

public class MaintenanceControllerTests
{
    private (SeriesContext, ActionsContext) CreateContexts()
    {
        var mangaOptions = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var actionsOptions = new DbContextOptionsBuilder<ActionsContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return (new SeriesContext(mangaOptions), new ActionsContext(actionsOptions));
    }

    private static MaintenanceController CreateController(SeriesContext mangaCtx, ActionsContext actionsCtx)
    {
        var controller = new MaintenanceController(mangaCtx, actionsCtx);
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public async Task CleanupActions_DeletesAllActions()
    {
        var (mangaCtx, actionsCtx) = CreateContexts();
        actionsCtx.Actions.Add(new API.Schema.ActionsContext.Actions.StartupActionRecord());
        await actionsCtx.SaveChangesAsync();

        var controller = CreateController(mangaCtx, actionsCtx);
        var result = await controller.CleanupActions();

        Assert.Equal(1, result.Value);
        Assert.Empty(await actionsCtx.Actions.ToListAsync());
    }

    [Fact]
    public async Task CleanupNoDownloadManga_RemovesUntrackedManga()
    {
        var (mangaCtx, actionsCtx) = CreateContexts();
        var untracked = new Series("Untracked", "Desc", "http://example.com/cover.jpg", SeriesReleaseStatus.Continuing, [], [], [], []);
        mangaCtx.Series.Add(untracked);
        await mangaCtx.SaveChangesAsync();

        var controller = CreateController(mangaCtx, actionsCtx);
        var result = await controller.CleanupNoDownloadManga();

        Assert.IsType<Ok>(result.Result);
        Assert.Empty(await mangaCtx.Series.ToListAsync());
    }

    [Fact]
    public async Task CleanupOrphanedFiles_EnqueuesADryRunCleanupJob()
    {
        var (mangaCtx, actionsCtx) = CreateContexts();
        var jobStore = new InMemoryJobStore();
        var controller = CreateController(mangaCtx, actionsCtx);

        var result = await controller.CleanupOrphanedFiles(jobStore, new SystemClock(), dryRun: true);

        Assert.IsType<Ok>(result);
        var job = Assert.Single(await jobStore.GetAllAsync());
        Assert.Equal(CleanupHandler.Type, job.Type);
        var payload = System.Text.Json.JsonSerializer.Deserialize<CleanupPayload>(job.Payload)!;
        Assert.Equal(CleanupKind.OrphanedFiles, payload.Kind);
        Assert.True(payload.DryRun);
    }

    [Fact]
    public async Task ResetAndResolveVolumes_ClearsAllVolumeNumbers()
    {
        var (mangaCtx, actionsCtx) = CreateContexts();
        var library = new FileLibrary("/tmp/test", "Test Library");
        mangaCtx.FileLibraries.Add(library);
        var manga = new Series("Test Series", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        mangaCtx.Series.Add(manga);
        mangaCtx.Chapters.Add(new Chapter(manga, "1", 3, null) { Downloaded = true, FileName = "test1.cbz" });
        mangaCtx.Chapters.Add(new Chapter(manga, "2", 3, null) { Downloaded = true, FileName = "test2.cbz" });
        await mangaCtx.SaveChangesAsync();

        var controller = CreateController(mangaCtx, actionsCtx);
        var result = await controller.ResetAndResolveVolumes(
            new InMemoryJobStore(), new KenkuSettings(), new SystemClock());

        Assert.IsType<Ok>(result.Result);
        var chapters = await mangaCtx.Chapters.ToListAsync();
        Assert.All(chapters, c => Assert.Null(c.VolumeNumber));
    }

    [Fact]
    public async Task ResetAndResolveVolumes_WhenStrategyDisabled_ClearsVolumes_ButEnqueuesNothing()
    {
        // The endpoint clears volumes regardless of strategy, but resolution being disabled means there is
        // nothing to re-resolve, so no jobs are enqueued (the disabled check is at enqueue time now).
        var (mangaCtx, actionsCtx) = CreateContexts();
        var library = new FileLibrary("/tmp/test", "Test Library");
        mangaCtx.FileLibraries.Add(library);
        var manga = new Series("Test", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        mangaCtx.Series.Add(manga);
        mangaCtx.Chapters.Add(new Chapter(manga, "1", 3, null) { Downloaded = true, FileName = "test1.cbz" });
        await mangaCtx.SaveChangesAsync();

        var jobStore = new InMemoryJobStore();
        var controller = CreateController(mangaCtx, actionsCtx);
        var result = await controller.ResetAndResolveVolumes(
            jobStore, new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.Disabled }, new SystemClock());

        Assert.IsType<Ok>(result.Result);
        Assert.All(await mangaCtx.Chapters.ToListAsync(), c => Assert.Null(c.VolumeNumber));
        Assert.Empty(await jobStore.GetAllAsync());
    }

    [Fact]
    public async Task ResetAndResolveVolumes_AlsoClearsVolumesOnNonDownloadedChapters()
    {
        // The clear is a bulk operation on all chapters — even undownloaded ones.
        // The resolver only re-populates downloaded chapters, so non-downloaded chapters
        // permanently lose their volume metadata after a reset.
        var (mangaCtx, actionsCtx) = CreateContexts();
        var library = new FileLibrary("/tmp/test", "Test Library");
        mangaCtx.FileLibraries.Add(library);
        var manga = new Series("Test", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        mangaCtx.Series.Add(manga);
        mangaCtx.Chapters.Add(new Chapter(manga, "1", 2, null) { Downloaded = true, FileName = "test1.cbz" });
        mangaCtx.Chapters.Add(new Chapter(manga, "2", 2, null) { Downloaded = false, FileName = null });
        await mangaCtx.SaveChangesAsync();

        var controller = CreateController(mangaCtx, actionsCtx);
        await controller.ResetAndResolveVolumes(
            new InMemoryJobStore(), new KenkuSettings(), new SystemClock());

        var chapters = await mangaCtx.Chapters.ToListAsync();
        Assert.All(chapters, c => Assert.Null(c.VolumeNumber));
    }

    [Fact]
    public async Task ResetAndResolveVolumes_EnqueuesAResolveJobPerSeries()
    {
        var (mangaCtx, actionsCtx) = CreateContexts();
        var library = new FileLibrary("/tmp/test", "Test Library");
        mangaCtx.FileLibraries.Add(library);
        var manga = new Series("Test", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        mangaCtx.Series.Add(manga);
        mangaCtx.Chapters.Add(new Chapter(manga, "1", 3, null) { Downloaded = true, FileName = "test1.cbz" });
        await mangaCtx.SaveChangesAsync();

        var jobStore = new InMemoryJobStore();
        var controller = CreateController(mangaCtx, actionsCtx);
        var result = await controller.ResetAndResolveVolumes(jobStore, new KenkuSettings(), new SystemClock());

        Assert.IsType<Ok>(result.Result);
        Assert.Single(await jobStore.GetAllAsync());
    }
}
