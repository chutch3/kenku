using API.Services.Interfaces;
using API.Acquirers.Interfaces;
using API.Controllers.DTOs;
using API.Controllers.Responses;
using API.Controllers.Requests;
using API.Connectors;
using API.Schema.SeriesContext;
using API.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;
using Chapter = API.Controllers.DTOs.Chapter;
using MangaConnectorImpl = API.Connectors.SeriesSource;


// ReSharper disable InconsistentNaming

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class ChaptersController(SeriesContext context, KenkuSettings settings, IEnumerable<MangaConnectorImpl> connectors, IChapterThumbnailService chapterThumbnailService) : ControllerBase
{
    /// <summary>
    /// Returns all <see cref="Schema.SeriesContext.Chapter"/> of <see cref="Schema.SeriesContext.Series"/> with <paramref name="MangaId"/>
    /// </summary>
    /// <param name="MangaId"><see cref="Schema.SeriesContext.Series"/>.Key</param>
    /// <param name="filter"></param>
    /// <param name="page">Page to request (default 1)</param>
    /// <param name="pageSize">Size of Page (default 10)</param>
    /// <response code="200"></response>
    /// <response code="400">Page data wrong</response>
    /// <response code="500">Error during Database request</response>
    [HttpPost("Series/{MangaId}")]
    [ProducesResponseType<PagedResponse<Chapter>>(Status200OK, "application/json")]
    [ProducesResponseType(Status400BadRequest)]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<PagedResponse<Chapter>>, BadRequest, InternalServerError>> GetChapters(string MangaId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]ChapterFilterRecord? filter = null, [FromQuery]int page = 1, [FromQuery]int pageSize = 10)
    {
        if (page < 1 || pageSize < 1)
            return TypedResults.BadRequest();

        IQueryable<Schema.SeriesContext.Chapter> queryable = context.Chapters
            .Include(ch => ch.SourceIds)
            .Where(ch => ch.ParentMangaId == MangaId);

        if (filter is not null)
        {
            if(filter.Downloaded.HasValue)
                queryable = queryable.Where(ch => ch.Downloaded == filter.Downloaded.Value);
            if(filter.Name is not null && !string.IsNullOrWhiteSpace(filter.Name))
                queryable = queryable.Where(ch => ch.Title != null && ch.Title.Contains(filter.Name));
            if(filter.VolumeNumber is not null)
                queryable = queryable.Where(ch => ch.VolumeNumber == filter.VolumeNumber);
            if(filter.ChapterNumber is not null && !string.IsNullOrWhiteSpace(filter.ChapterNumber))
                queryable = queryable.Where(ch => ch.ChapterNumber == filter.ChapterNumber);
        }

        if (await queryable.ToListAsync(HttpContext.RequestAborted) is not { } dbChapters)
            return TypedResults.InternalServerError();
        PagedResponse<Chapter> pagedResponse = dbChapters.OrderDescending().CreatePagedResponse(page, pageSize)
            .ToType(c =>
            {
                IEnumerable<DTOs.SourceId<Chapter>> ids = c.SourceIds.Select(id =>
                    DTOs.SourceId<Chapter>.From(id));
                return new Chapter(c.Key, c.ParentMangaId, c.VolumeNumber, c.ChapterNumber, c.Title, ids, c.Downloaded,
                    c.FileName);
            });

        return TypedResults.Ok(pagedResponse);
    }

    /// <summary>
    /// Returns the latest <see cref="Chapter"/> of requested <see cref="Schema.SeriesContext.Series"/>
    /// </summary>
    /// <param name="MangaId"><see cref="Schema.SeriesContext.Series"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="204">No available chapters</response>
    /// <response code="404"><see cref="Schema.SeriesContext.Series"/> with <paramref name="MangaId"/> not found.</response>
    [HttpGet("LatestAvailable/{MangaId}")]
    [ProducesResponseType<int>(Status200OK, "application/json")]
    [ProducesResponseType(Status204NoContent)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<Chapter>, NoContent, NotFound<string>>> GetLatestChapter(string MangaId)
    {
        // 1. Explicitly check if the parent Series actually exists
        if (!await context.Series.AnyAsync(m => m.Key == MangaId, HttpContext.RequestAborted))
            return TypedResults.NotFound(nameof(MangaId));

        // 2. Fetch the chapters
        var dbChapters = await context.Chapters.Include(ch => ch.SourceIds)
            .Where(ch => ch.ParentMangaId == MangaId)
            .ToListAsync(HttpContext.RequestAborted);

        Schema.SeriesContext.Chapter? c = dbChapters.Max();

        // 3. If Series exists but has 0 chapters, return NoContent
        if (c is null)
            return TypedResults.NoContent();

        IEnumerable<DTOs.SourceId<Chapter>> ids = c.SourceIds.Select(id =>
            DTOs.SourceId<Chapter>.From(id));

        return TypedResults.Ok(new Chapter(c.Key, c.ParentMangaId, c.VolumeNumber, c.ChapterNumber, c.Title, ids, c.Downloaded, c.FileName));
    }
    /// <summary>
    /// Returns the latest <see cref="Chapter"/> of requested <see cref="Schema.SeriesContext.Series"/> that is downloaded
    /// </summary>
    /// <param name="MangaId"><see cref="Schema.SeriesContext.Series"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="204">No available chapters</response>
    /// <response code="404"><see cref="Schema.SeriesContext.Series"/> with <paramref name="MangaId"/> not found.</response>
    /// <response code="412">Could not retrieve the maximum chapter-number</response>
    /// <response code="503">Retry after timeout, updating value</response>
    [HttpGet("LatestDownloaded/{MangaId}")]
    [ProducesResponseType<Chapter>(Status200OK, "application/json")]
    [ProducesResponseType(Status204NoContent)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status412PreconditionFailed)]
    [ProducesResponseType(Status503ServiceUnavailable)]
    public async Task<Results<Ok<Chapter>, NoContent, NotFound<string>, StatusCodeHttpResult>>  GetLatestChapterDownloaded(string MangaId)
    {
        if(await context.Chapters.Include(ch => ch.SourceIds)
               .Where(ch => ch.ParentMangaId == MangaId && ch.Downloaded)
               .ToListAsync(HttpContext.RequestAborted)
           is not { } dbChapters)
            return TypedResults.NotFound(nameof(MangaId));

        Schema.SeriesContext.Chapter? c = dbChapters.Max();
        if (c is null)
            return TypedResults.NoContent();

        IEnumerable<DTOs.SourceId<Chapter>> ids = c.SourceIds.Select(id =>
            DTOs.SourceId<Chapter>.From(id));
        return TypedResults.Ok(new Chapter(c.Key, c.ParentMangaId, c.VolumeNumber, c.ChapterNumber, c.Title, ids, c.Downloaded, c.FileName));
    }

    /// <summary>
    /// Configure the <see cref="Chapter"/> cut-off for <see cref="Schema.SeriesContext.Series"/>
    /// </summary>
    /// <param name="MangaId"><see cref="Schema.SeriesContext.Series"/>.Key</param>
    /// <param name="chapterThreshold">Threshold (<see cref="Chapter"/> ChapterNumber)</param>
    /// <response code="202"></response>
    /// <response code="404"><see cref="Schema.SeriesContext.Series"/> with <paramref name="MangaId"/> not found.</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPatch("IgnoreBefore/{MangaId}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok, NotFound<string>, InternalServerError<string>>> IgnoreChaptersBefore(string MangaId, [FromBody]float chapterThreshold)
    {
        if (await context.Series.FirstOrDefaultAsync(m => m.Key == MangaId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(MangaId));

        manga.IgnoreChaptersBefore = chapterThreshold;
        if(await context.Sync(HttpContext.RequestAborted, GetType(), System.Reflection.MethodBase.GetCurrentMethod()?.Name) is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        return TypedResults.Ok();
    }

    /// <summary>
    /// Returns <see cref="Chapter"/> with <paramref name="ChapterId"/>
    /// </summary>
    /// <param name="ChapterId"><see cref="Chapter"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Chapter"/> with <paramref name="ChapterId"/> not found</response>
    [HttpGet("{ChapterId}")]
    [ProducesResponseType<Chapter>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<Chapter>, NotFound<string>>> GetChapter (string ChapterId)
    {
        if (await context.Chapters.FirstOrDefaultAsync(c => c.Key == ChapterId, HttpContext.RequestAborted) is not { } chapter)
            return TypedResults.NotFound(nameof(ChapterId));

        IEnumerable<DTOs.SourceId<Chapter>> ids = chapter.SourceIds.Select(id =>
            DTOs.SourceId<Chapter>.From(id));
        return TypedResults.Ok(new Chapter(chapter.Key, chapter.ParentMangaId, chapter.VolumeNumber, chapter.ChapterNumber, chapter.Title,ids, chapter.Downloaded, chapter.FileName));
    }

    /// <summary>
    /// Updates mutable metadata (<see cref="Schema.SeriesContext.Chapter.FileName"/> and <see cref="Schema.SeriesContext.Chapter.VolumeNumber"/>) on a <see cref="Chapter"/>
    /// </summary>
    /// <param name="ChapterId"><see cref="Chapter"/>.Key</param>
    /// <param name="patch">Fields to update</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Chapter"/> with <paramref name="ChapterId"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPatch("{ChapterId}")]
    public async Task<Results<Ok, NotFound<string>, InternalServerError<string>>> UpdateChapter(string ChapterId, [FromBody] PatchChapterRecord patch,
        [FromServices] API.JobRuntime.Interfaces.IJobStore jobStore, [FromServices] API.JobRuntime.Interfaces.IClock clock)
    {
        if (await context.Chapters.FirstOrDefaultAsync(c => c.Key == ChapterId, HttpContext.RequestAborted) is not { } chapter)
            return TypedResults.NotFound(nameof(ChapterId));

        string? oldFileName = chapter.FileName;
        bool fileNameChanged = patch.FileName != oldFileName;

        // Move the file on disk via a background MoveData job.
        if (fileNameChanged && oldFileName is not null && patch.FileName is not null)
            await jobStore.EnqueueAsync(new API.Schema.JobsContext.Job(
                API.JobRuntime.Handlers.MoveDataHandler.Type,
                API.JobRuntime.Handlers.MoveDataHandler.PayloadFor(oldFileName, patch.FileName), clock.UtcNow,
                dedupKey: API.JobRuntime.Handlers.MoveDataHandler.DedupKey(patch.FileName)), HttpContext.RequestAborted);

        chapter.FileName = patch.FileName;
        chapter.VolumeNumber = patch.VolumeNumber;

        if (await context.Sync(HttpContext.RequestAborted, GetType(), System.Reflection.MethodBase.GetCurrentMethod()?.Name) is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        return TypedResults.Ok();
    }

    /// <summary>
    /// Deletes <see cref="Chapter"/> with <paramref name="ChapterId"/>
    /// </summary>
    /// <param name="ChapterId"><see cref="Chapter"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Chapter"/> with <paramref name="ChapterId"/> not found</response>
    [HttpDelete("{ChapterId}")]
    public async Task<Results<Ok, NotFound<string>>> DeleteChapter(string ChapterId)
    {
        var chapter = await context.Chapters.FirstOrDefaultAsync(c => c.Key == ChapterId, HttpContext.RequestAborted);
        if (chapter == null)
            return TypedResults.NotFound(nameof(ChapterId));

        context.Chapters.Remove(chapter);
        await context.SaveChangesAsync(HttpContext.RequestAborted);

        return TypedResults.Ok();
    }

    /// <summary>
    /// Returns the <see cref="DTOs.SourceId{Chapter}"/> with <see cref="DTOs.SourceId{Chapter}"/>.Key
    /// </summary>
    /// <param name="MangaConnectorIdId">Key of <see cref="DTOs.SourceId{Chapter}"/></param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="DTOs.SourceId{Chapter}"/> with <paramref name="MangaConnectorIdId"/> not found</response>
    [HttpGet("ConnectorId/{MangaConnectorIdId}")]
    [ProducesResponseType<DTOs.SourceId<Chapter>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<DTOs.SourceId<Chapter>>, NotFound<string>>> GetChapterSourceId (string MangaConnectorIdId)
    {
        if (await context.MangaConnectorToChapter.FirstOrDefaultAsync(c => c.Key == MangaConnectorIdId, HttpContext.RequestAborted) is not { } mcIdManga)
            return TypedResults.NotFound(nameof(MangaConnectorIdId));

        DTOs.SourceId<Chapter> result = new (mcIdManga.Key, mcIdManga.MangaConnectorName, mcIdManga.ObjId, mcIdManga.IdOnConnectorSite, mcIdManga.WebsiteUrl, mcIdManga.UseForDownload);

        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Deletes the <see cref="DTOs.SourceId{Chapter}"/> with <see cref="DTOs.SourceId{Chapter}"/>.Key
    /// </summary>
    /// <param name="MangaConnectorIdId">Key of <see cref="DTOs.SourceId{Chapter}"/></param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="DTOs.SourceId{Chapter}"/> with <paramref name="MangaConnectorIdId"/> not found</response>
    [HttpDelete("ConnectorId/{MangaConnectorIdId}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok, NotFound<string>>> DeleteChapterSourceId (string MangaConnectorIdId)
    {
        if (await context.MangaConnectorToChapter.Where(c => c.Key == MangaConnectorIdId).ExecuteDeleteAsync(HttpContext.RequestAborted) < 1)
            return TypedResults.NotFound(nameof(MangaConnectorIdId));
        return TypedResults.Ok();
    }

    /// <summary>
    /// (Un-)Marks <see cref="Chapter"/> as requested for Download from <see cref="API.Connectors.SeriesSource"/>
    /// </summary>
    /// <param name="ChapterId"><see cref="Chapter"/> with <paramref name="ChapterId"/></param>
    /// <param name="MangaConnectorName"><see cref="API.Connectors.SeriesSource"/> with <paramref name="MangaConnectorName"/></param>
    /// <param name="IsRequested">true to mark as requested, false to mark as not-requested</param>
    /// <response code="200"></response>
    /// <response code="404"><paramref name="ChapterId"/> or <paramref name="MangaConnectorName"/> not found</response>
    /// <response code="428"><see cref="Chapter"/> is not linked to <see cref="API.Connectors.SeriesSource"/> yet. Search for <see cref="Chapter"/> on <see cref="API.Connectors.SeriesSource"/> first (to create a <see cref="DTOs.SourceId{T}"/>).</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPatch("{ChapterId}/DownloadFrom/{MangaConnectorName}/{IsRequested}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound,  "text/plain")]
    [ProducesResponseType<string>(Status428PreconditionRequired,  "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError,  "text/plain")]
    public async Task<Results<Ok, NotFound<string>, StatusCodeHttpResult, InternalServerError<string>>> MarkAsRequested(string ChapterId, string MangaConnectorName, bool IsRequested, [FromServices] API.JobRuntime.Interfaces.IJobStore jobStore, [FromServices] API.JobRuntime.Interfaces.IClock clock)
    {
        if (await context.Chapters.FirstOrDefaultAsync(ch => ch.Key == ChapterId, HttpContext.RequestAborted) is not { } chapter)
            return TypedResults.NotFound(nameof(ChapterId));
        if(!connectors.Any(c => c.Name.Equals(MangaConnectorName, StringComparison.InvariantCultureIgnoreCase)))
            return TypedResults.NotFound(nameof(MangaConnectorName));

        if (await context.MangaConnectorToChapter
                .FirstOrDefaultAsync(id => id.MangaConnectorName == MangaConnectorName && id.ObjId == ChapterId, HttpContext.RequestAborted)
            is not { } chId)
        {
            return TypedResults.StatusCode(Status428PreconditionRequired);
        }

        chId.UseForDownload = IsRequested;
        if(await context.Sync(HttpContext.RequestAborted, GetType(), System.Reflection.MethodBase.GetCurrentMethod()?.Name) is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        if (IsRequested)
            await jobStore.EnqueueAsync(new API.Schema.JobsContext.Job(
                API.JobRuntime.Handlers.DownloadChapterHandler.Type,
                API.JobRuntime.Handlers.DownloadChapterHandler.PayloadFor(chId.Key), clock.UtcNow,
                resourceKey: chapter.ParentMangaId, dedupKey: API.JobRuntime.Reconcilers.DownloadReconciler.DedupKey(chId.Key),
                maxAttempts: settings.DownloadMaxAttempts),
                HttpContext.RequestAborted);

        return TypedResults.Ok();
    }

    /// <summary>
    /// Manually assigns a volume number to a <see cref="Schema.SeriesContext.Chapter"/>.
    /// Passing null clears the volume assignment and confidence.
    /// </summary>
    /// <param name="ChapterId"><see cref="Schema.SeriesContext.Chapter"/>.Key</param>
    /// <param name="patch">Volume number to assign (null to clear)</param>
    /// <response code="200">Volume number updated</response>
    /// <response code="404"><see cref="Schema.SeriesContext.Chapter"/> with <paramref name="ChapterId"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPut("{ChapterId}/volume")]
    [ProducesResponseType<DTOs.ChapterVolumeAssignmentResult>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok<DTOs.ChapterVolumeAssignmentResult>, NotFound<string>, InternalServerError<string>>> AssignChapterVolume(string ChapterId, [FromBody] PatchChapterVolumeRecord patch)
    {
        if (await context.Chapters.FirstOrDefaultAsync(c => c.Key == ChapterId, HttpContext.RequestAborted) is not { } chapter)
            return TypedResults.NotFound(nameof(ChapterId));

        if (patch.VolumeNumber is not null)
        {
            chapter.VolumeNumber = patch.VolumeNumber;
            chapter.MetadataConfidence = Schema.SeriesContext.MetadataConfidence.Manual;
        }
        else
        {
            chapter.VolumeNumber = null;
            chapter.MetadataConfidence = null;
        }

        if (await context.Sync(HttpContext.RequestAborted, GetType(), System.Reflection.MethodBase.GetCurrentMethod()?.Name) is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        return TypedResults.Ok(new DTOs.ChapterVolumeAssignmentResult(
            chapter.Key,
            chapter.ChapterNumber,
            chapter.VolumeNumber,
            chapter.MetadataConfidence?.ToString()
        ));
    }

    /// <summary>
    /// Returns a 200×300 JPEG preview thumbnail of the first page of the chapter archive.
    /// The thumbnail is generated lazily and cached at <c>{AppData}/previews/{chapter.Key}.jpg</c>.
    /// </summary>
    /// <param name="ChapterId"><see cref="Schema.SeriesContext.Chapter"/>.Key</param>
    /// <response code="200">JPEG thumbnail, 200×300 pixels</response>
    /// <response code="404">Chapter not found, archive unreadable, no images in archive, or chapter is bundled</response>
    [HttpGet("{ChapterId}/preview")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<FileStreamHttpResult, NotFound<string>>> GetChapterPreview(string ChapterId)
    {
        if (await context.Chapters.FirstOrDefaultAsync(c => c.Key == ChapterId, HttpContext.RequestAborted) is not { } chapter)
            return TypedResults.NotFound("Chapter not found");

        if (chapter.IsBundled)
            return TypedResults.NotFound("bundled chapter thumbnails not yet supported");

        string cachePath = Path.Combine(settings.AppData, "previews", $"{chapter.Key}.jpg");

        if (System.IO.File.Exists(cachePath))
        {
            var cachedStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return TypedResults.Stream(cachedStream, "image/jpeg");
        }

        string? archivePath = chapter.FullArchiveFilePath;
        if (string.IsNullOrEmpty(archivePath))
            return TypedResults.NotFound("Chapter archive path could not be resolved");

        bool generated = await chapterThumbnailService.GenerateThumbnailAsync(archivePath, cachePath, HttpContext.RequestAborted);
        if (!generated)
            return TypedResults.NotFound("Could not generate thumbnail: archive unreadable or contains no images");

        var thumbnailStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return TypedResults.Stream(thumbnailStream, "image/jpeg");
    }

    /// <summary>
    /// The pickable downloads for a chapter whose post bundles several (scan variants or a true
    /// multi-issue bundle), resolved live from the post so the answer reflects it right now.
    /// </summary>
    /// <response code="200">Options to pick from; empty with a reason when the post needs manual handling</response>
    /// <response code="404">Chapter source not found</response>
    [HttpGet("{ChapterSourceKey}/DownloadOptions")]
    [ProducesResponseType<DownloadOptionsResponse>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<DownloadOptionsResponse>, NotFound<string>>> GetDownloadOptions(string ChapterSourceKey)
    {
        if (await context.MangaConnectorToChapter.Include(id => id.Obj)
                .FirstOrDefaultAsync(id => id.Key == ChapterSourceKey, HttpContext.RequestAborted) is not { } chId)
            return TypedResults.NotFound(nameof(ChapterSourceKey));
        return TypedResults.Ok(await ResolveDownloadOptions(chId));
    }

    /// <summary>Resolves a chapter's post live into its pickable downloads (shared by the options
    /// endpoint and the pinned-download validation).</summary>
    private async Task<DownloadOptionsResponse> ResolveDownloadOptions(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter> chId)
    {
        if (connectors.FirstOrDefault(c => c.Name.Equals(chId.MangaConnectorName, StringComparison.OrdinalIgnoreCase))
            is not IArchiveUrlResolver resolver)
            return new DownloadOptionsResponse([], "this source resolves its downloads automatically");

        return await resolver.ResolveArchiveUrl(chId, HttpContext.RequestAborted) switch
        {
            ArchiveResolution.Choice choice => new DownloadOptionsResponse(choice.Options, null),
            ArchiveResolution.Resolved resolved => new DownloadOptionsResponse([new DownloadOption("Download", resolved.Url, null)], null),
            ArchiveResolution.Manual manual => new DownloadOptionsResponse([], manual.Reason),
            _ => new DownloadOptionsResponse([], "unknown resolution"),
        };
    }

    /// <summary>
    /// Enqueues a download pinned to one of the chapter's <see cref="DownloadOptionsResponse"/>
    /// options. The URL is re-validated against a live resolve, so a stale pick is rejected rather
    /// than fetching something the post no longer offers.
    /// </summary>
    /// <response code="200">Pinned download job enqueued</response>
    /// <response code="400">The post no longer offers that URL</response>
    /// <response code="404">Chapter source not found</response>
    [HttpPost("{ChapterSourceKey}/Download")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok, BadRequest<string>, NotFound<string>>> Download(string ChapterSourceKey,
        [FromBody] PinnedDownloadRequest request,
        [FromServices] API.JobRuntime.Interfaces.IJobStore jobStore,
        [FromServices] API.JobRuntime.Interfaces.IClock clock)
    {
        if (await context.MangaConnectorToChapter.Include(id => id.Obj)
                .FirstOrDefaultAsync(id => id.Key == ChapterSourceKey, HttpContext.RequestAborted) is not { } chId)
            return TypedResults.NotFound(nameof(ChapterSourceKey));

        DownloadOptionsResponse offered = await ResolveDownloadOptions(chId);
        if (!offered.Options.Any(o => o.Url == request.Url))
            return TypedResults.BadRequest("the post no longer offers that download — refresh the options");

        await jobStore.EnqueueAsync(new API.Schema.JobsContext.Job(
            API.JobRuntime.Handlers.DownloadChapterHandler.Type,
            API.JobRuntime.Handlers.DownloadChapterHandler.PayloadFor(chId.Key, request.Url),
            clock.UtcNow,
            resourceKey: chId.Obj.ParentMangaId,
            dedupKey: $"pinned-download:{chId.Key}"), HttpContext.RequestAborted);
        return TypedResults.Ok();
    }
}
