using API.JobRuntime.Reconcilers;
using API.JobRuntime.Interfaces;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Services;
using API.Schema.ActionsContext;
using API.Schema.SeriesContext;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class MaintenanceController(SeriesContext mangaContext, ActionsContext actionContext) : ControllerBase
{
    
    /// <summary>
    /// Removes all <see cref="Series"/> not marked for Download on any <see cref="SeriesSource"/>
    /// </summary>
    /// <response code="200"></response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPost("CleanupNoDownloadManga")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok, InternalServerError<string>>> CleanupNoDownloadManga()
    {
        if (await mangaContext.Series
                .Include(m => m.SourceIds)
                .Where(m => !m.SourceIds.Any(id => id.UseForDownload))
                .ToListAsync(HttpContext.RequestAborted) is not { } remove)
            return TypedResults.InternalServerError("Database error");
        
        mangaContext.RemoveRange(remove);
        
        if(await mangaContext.Sync(HttpContext.RequestAborted, GetType(), System.Reflection.MethodBase.GetCurrentMethod()?.Name) is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);
        return TypedResults.Ok();
    }
    
    
    /// <summary>
    /// Removes all <see cref="ActionRecord"/>
    /// </summary>
    /// <response code="200">Number of deleted records</response>
    [HttpPost("CleanupActions")]
    [ProducesResponseType<int>(Status200OK, "text/plain")]
    public async Task<Ok<int>> CleanupActions()
    {
        var actions = await actionContext.Actions.ToListAsync(HttpContext.RequestAborted);
        int count = actions.Count;
        actionContext.Actions.RemoveRange(actions);
        await actionContext.Sync(HttpContext.RequestAborted, GetType(), "CleanupActions");
        return TypedResults.Ok(count);
    }

    /// <summary>
    /// Enqueues a Cleanup (OrphanedFiles) job to remove files from the library that are not tracked in the database.
    /// </summary>
    /// <param name="dryRun">If true, only log what would be deleted without actually deleting it.</param>
    /// <param name="force">If true, bypass the safety guards that skip wiping an untracked library or
    /// deleting a majority of a library's archives. Use only for a deliberate bulk cleanup.</param>
    /// <response code="200">Cleanup job enqueued</response>
    [HttpPost("CleanupOrphanedFiles")]
    [ProducesResponseType(Status200OK)]
    public async Task<Ok> CleanupOrphanedFiles([FromServices] IJobStore jobStore, [FromServices] IClock clock, bool dryRun = false, bool force = false)
    {
        await jobStore.EnqueueAsync(new Schema.JobsContext.Job(
            CleanupHandler.Type, CleanupHandler.PayloadFor(CleanupKind.OrphanedFiles, dryRun, force), clock.UtcNow),
            HttpContext.RequestAborted);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Enqueues a ResolveSeriesVolumes job for each series with unresolved chapter volumes.
    /// </summary>
    /// <response code="200">Resolve jobs enqueued</response>
    [HttpPost("ResolveMissingVolumes")]
    [ProducesResponseType(Status200OK)]
    public async Task<Ok> ResolveMissingVolumes([FromServices] IJobStore jobStore, [FromServices] KenkuSettings settings,
        [FromServices] IClock clock)
    {
        await VolumeResolutionReconciler.ScanAndEnqueueAsync(mangaContext, jobStore, settings, clock.UtcNow, HttpContext.RequestAborted);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Enqueues a PlaceChapterFile job for each chapter whose stored filename no longer matches the current naming scheme.
    /// </summary>
    /// <response code="200">Placement jobs enqueued</response>
    [HttpPost("SyncChapterFileNames")]
    [ProducesResponseType(Status200OK)]
    public async Task<Ok> SyncChapterFileNames([FromServices] IJobStore jobStore, [FromServices] KenkuSettings settings,
        [FromServices] IClock clock)
    {
        await ChapterFilePlacementReconciler.ScanAndEnqueueAsync(mangaContext, jobStore, settings, clock.UtcNow, HttpContext.RequestAborted);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Clears all chapter volume numbers and enqueues ResolveSeriesVolumes jobs to re-resolve them from scratch.
    /// </summary>
    /// <param name="workerQueue"></param>
    /// <param name="settings"></param>
    /// <param name="mangaDexVolumeResolver"></param>
    /// <response code="200">Volumes cleared and resolve worker queued</response>
    /// <response code="500">Error during database operation</response>
    [HttpPost("ResetAndResolveVolumes")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok, InternalServerError<string>>> ResetAndResolveVolumes(
        [FromServices] IJobStore jobStore,
        [FromServices] KenkuSettings settings,
        [FromServices] IClock clock)
    {
        var chapters = await mangaContext.Chapters.ToListAsync(HttpContext.RequestAborted);
        foreach (var chapter in chapters)
            chapter.VolumeNumber = null;

        if (await mangaContext.Sync(HttpContext.RequestAborted, GetType(), nameof(ResetAndResolveVolumes)) is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        await VolumeResolutionReconciler.ScanAndEnqueueAsync(mangaContext, jobStore, settings, clock.UtcNow, HttpContext.RequestAborted);
        return TypedResults.Ok();
    }
}