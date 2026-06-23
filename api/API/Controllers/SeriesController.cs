using API.Controllers.DTOs;
using API.Connectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.SeriesContext;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Soenneker.Utils.String.NeedlemanWunsch;
using static Microsoft.AspNetCore.Http.StatusCodes;
using AltTitle = API.Controllers.DTOs.AltTitle;
using Author = API.Controllers.DTOs.Author;
using Chapter = API.Schema.SeriesContext.Chapter;
using Link = API.Controllers.DTOs.Link;
using Series = API.Controllers.DTOs.Series;
using SeriesSourceImpl = API.Connectors.SeriesSource;

// ReSharper disable InconsistentNaming

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class SeriesController(SeriesContext context, ActionsContext actionsContext, KenkuSettings settings, IEnumerable<SeriesSourceImpl> connectors) : ControllerBase
{
    
    /// <summary>
    /// Returns all cached <see cref="DTOs.Series"/>
    /// </summary>
    /// <response code="200"><see cref="MinimalSeries"/> exert of <see cref="Schema.SeriesContext.Series"/>. Use <see cref="GetSeries"/> for more information</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpGet]
    [ProducesResponseType<List<MinimalSeries>>(Status200OK, "application/json")]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<List<MinimalSeries>>, InternalServerError>> GetAllSeries ()
    {
        if (await context.GetTrackedSeries()
                .OrderBy(m => m.Name)
                .ToArrayAsync(HttpContext.RequestAborted) is not
            { } result)
            return TypedResults.InternalServerError();
        
        return TypedResults.Ok(result.Select(MinimalSeries.From).ToList());
    }
    
    /// <summary>
    /// Returns all <see cref="Schema.SeriesContext.Series"/> that are being downloaded from at least one <see cref="API.Connectors.SeriesSource"/>
    /// </summary>
    /// <response code="200"><see cref="MinimalSeries"/> exert of <see cref="Schema.SeriesContext.Series"/>. Use <see cref="GetSeries"/> for more information</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpGet("Downloading")]
    [ProducesResponseType<MinimalSeries[]>(Status200OK, "application/json")]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<List<MinimalSeries>>, InternalServerError>> GetSeriesDownloading()
    {
        if (await context.Series
                .Include(m => m.SourceIds)
                .Where(m => m.SourceIds.Any(id => id.UseForDownload))
                .OrderBy(m => m.Name)
                .ToArrayAsync(HttpContext.RequestAborted) is not { } result)
            return TypedResults.InternalServerError();

        return TypedResults.Ok(result.Select(MinimalSeries.From).ToList());
    }

    /// <summary>
    /// Per-series operational rollup: actual download progress, live job counts, and the most recent
    /// failure — the data the library badge derives its state from.
    /// </summary>
    /// <response code="200">One <see cref="SeriesRollup"/> per series.</response>
    [HttpGet("Rollup")]
    [ProducesResponseType<List<SeriesRollup>>(Status200OK, "application/json")]
    public async Task<Ok<List<SeriesRollup>>> GetSeriesRollup(
        [FromServices] Schema.JobsContext.JobsContext jobsContext, [FromServices] API.Services.SeriesRollupService rollups)
    {
        return TypedResults.Ok(await rollups.GetAsync(context, jobsContext, actionsContext, HttpContext.RequestAborted));
    }

    /// <summary>
    /// Return <see cref="Schema.SeriesContext.Series"/> with <paramref name="SeriesId"/>
    /// </summary>
    /// <param name="SeriesId"><see cref="Schema.SeriesContext.Series"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Series"/> with <paramref name="SeriesId"/> not found</response>
    [HttpGet("{SeriesId}")]
    [ProducesResponseType<Series>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<Series>, NotFound<string>>> GetSeries (string SeriesId)
    {
        if (await context.SeriesWithMetadata().Include(m => m.SourceIds).FirstOrDefaultAsync(m => m.Key == SeriesId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(SeriesId));
        
        IEnumerable<DTOs.SourceId<Series>> ids = manga.SourceIds.Select(id => DTOs.SourceId<Series>.From(id));
        IEnumerable<Author> authors = manga.Authors.Select(a => new Author(a.Key, a.AuthorName));
        IEnumerable<string> tags = manga.SeriesTags.Select(t => t.Tag);
        IEnumerable<Link> links = manga.Links.Select(l => new Link(l.Key, l.LinkProvider, l.LinkUrl));
        IEnumerable<AltTitle> altTitles = manga.AltTitles.Select(a => new AltTitle(a.Language, a.Title));
        Series result = new (manga.Key, manga.Name, manga.Description, manga.ReleaseStatus, ids, manga.IgnoreChaptersBefore, manga.Year, manga.OriginalLanguage, authors, tags, links, altTitles, manga.LibraryId, manga.CoverUrl);
        
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Delete <see cref="Series"/> with <paramref name="SeriesId"/>
    /// </summary>
    /// <param name="SeriesId"><see cref="Series"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Series"/> with <paramref name="SeriesId"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpDelete("{SeriesId}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok, NotFound<string>, InternalServerError<string>>> DeleteSeries (string SeriesId,
        [FromServices] API.Services.SeriesLibraryService libraryService, [FromServices] Schema.JobsContext.JobsContext jobsContext)
    {
        if (!await libraryService.DeleteAsync(context, jobsContext, SeriesId, HttpContext.RequestAborted))
            return TypedResults.NotFound(nameof(SeriesId));
        return TypedResults.Ok();
    }


    /// <summary>
    /// Merge two <see cref="Series"/> into one. THIS IS NOT REVERSIBLE!
    /// </summary>
    /// <param name="SeriesIdFrom"><see cref="Series"/>.Key of <see cref="Series"/> merging data from (getting deleted)</param>
    /// <param name="SeriesIdInto"><see cref="Series"/>.Key of <see cref="Series"/> merging data into</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Series"/> with <paramref name="SeriesIdFrom"/> or <paramref name="SeriesIdInto"/> not found</response>
    [HttpPost("{SeriesIdFrom}/MergeInto/{SeriesIdInto}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok, NotFound<string>>> MergeIntoSeries (string SeriesIdFrom, string SeriesIdInto,
        [FromServices] API.JobRuntime.Interfaces.IJobStore jobStore, [FromServices] API.JobRuntime.Interfaces.IClock clock)
    {
        if (await context.SeriesIncludeAll().FirstOrDefaultAsync(m => m.Key == SeriesIdFrom, HttpContext.RequestAborted) is not { } from)
            return TypedResults.NotFound(nameof(SeriesIdFrom));
        if (await context.SeriesIncludeAll().FirstOrDefaultAsync(m => m.Key == SeriesIdInto, HttpContext.RequestAborted) is not { } into)
            return TypedResults.NotFound(nameof(SeriesIdInto));

        foreach ((string from_, string to) in into.MergeFrom(from, context))
            await jobStore.EnqueueAsync(new API.Schema.JobsContext.Job(
                API.JobRuntime.Handlers.MoveDataHandler.Type,
                API.JobRuntime.Handlers.MoveDataHandler.PayloadFor(from_, to), clock.UtcNow,
                dedupKey: API.JobRuntime.Handlers.MoveDataHandler.DedupKey(to)), HttpContext.RequestAborted);

        return TypedResults.Ok();
    }

    /// <summary>
    /// Returns Cover of <see cref="Series"/> with <paramref name="SeriesId"/>
    /// </summary>
    /// <param name="SeriesId"><see cref="Series"/>.Key</param>
    /// <param name="CoverSize">Size of the cover returned
    /// <br /> - <see cref="CoverSize.Small"/> <see cref="Constants.ImageSmSize"/>
    /// <br /> - <see cref="CoverSize.Medium"/> <see cref="Constants.ImageMdSize"/>
    /// <br /> - <see cref="CoverSize.Large"/> <see cref="Constants.ImageLgSize"/>
    /// </param>
    /// <response code="200">JPEG Image</response>
    /// <response code="204">Cover not loaded</response>
    /// <response code="404"><see cref="Series"/> with <paramref name="SeriesId"/> not found</response>
    /// <response code="503">Retry later, downloading cover</response>
    [HttpGet("{SeriesId}/Cover/{CoverSize?}")]
    [ProducesResponseType<FileContentResult>(Status200OK,"image/jpeg")]
    [ProducesResponseType(Status204NoContent)]
    [ProducesResponseType(Status400BadRequest)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status503ServiceUnavailable)]
    public async Task<Results<FileContentHttpResult, NoContent, BadRequest, NotFound<string>, StatusCodeHttpResult>> GetCover (string SeriesId, CoverSize? CoverSize = null)
    {
        if (await context.Series.FirstOrDefaultAsync(m => m.Key == SeriesId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(SeriesId));

        string cache = CoverSize switch
        {
            SeriesController.CoverSize.Small => settings.CoverImageCacheSmall,
            SeriesController.CoverSize.Medium => settings.CoverImageCacheMedium,
            SeriesController.CoverSize.Large => settings.CoverImageCacheLarge,
            _ => settings.CoverImageCacheOriginal
        };

        if (await manga.GetCoverImage(cache, HttpContext.RequestAborted) is not { } data)
        {
            return TypedResults.NotFound("Image not in cache");
        }
        
        DateTime lastModified = data.fileInfo.LastWriteTime;
        EntityTagHeaderValue entityTagHeaderValue = EntityTagHeaderValue.Parse($"\"{lastModified.Ticks}\"");
        if(HttpContext.Request.Headers.ETag.Equals(entityTagHeaderValue.Tag.Value))
            return TypedResults.StatusCode(Status304NotModified);
        HttpContext.Response.Headers.CacheControl = "public";
        return TypedResults.Bytes(data.stream.ToArray(), "image/jpeg", lastModified: new DateTimeOffset(lastModified), entityTag: entityTagHeaderValue);
    }
    public enum CoverSize { Original, Large, Medium, Small }

    /// <summary>
    /// Move <see cref="Series"/> to different <see cref="DTOs.FileLibrary"/>
    /// </summary>
    /// <param name="SeriesId"><see cref="Series"/>.Key</param>
    /// <param name="LibraryId"><see cref="DTOs.FileLibrary"/>.Key</param>
    /// <param name="connectorName">(Optional) Name of the connector to fetch manga from if not in DB</param>
    /// <param name="connectorSeriesId">(Optional) ID of the manga on the connector site</param>
    /// <response code="202">Folder is going to be moved</response>
    /// <response code="404"><paramref name="SeriesId"/> or <paramref name="LibraryId"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPost("{SeriesId}/ChangeLibrary/{LibraryId}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError,  "text/plain")]
    public async Task<Results<Ok, NotFound<string>, InternalServerError<string>>> ChangeLibrary(string SeriesId, string LibraryId,
        [FromServices] API.Services.SeriesLibraryService libraryService,
        [FromQuery] string? connectorName = null, [FromQuery] string? connectorSeriesId = null, [FromQuery] bool download = false,
        [FromQuery] string? coverUrl = null, [FromQuery] API.Schema.SeriesContext.LibraryLayout? layout = null)
    {
        (API.Services.ChangeLibraryStatus status, string? error) = await libraryService.ChangeLibraryAsync(
            context, actionsContext, SeriesId, LibraryId, connectorName, connectorSeriesId, download, HttpContext.RequestAborted, coverUrl, layout);
        return status switch
        {
            API.Services.ChangeLibraryStatus.Ok => TypedResults.Ok(),
            API.Services.ChangeLibraryStatus.LibraryNotFound => TypedResults.NotFound(nameof(LibraryId)),
            API.Services.ChangeLibraryStatus.SeriesNotFound => TypedResults.NotFound(nameof(SeriesId)),
            API.Services.ChangeLibraryStatus.ConnectorNotFound => TypedResults.NotFound(nameof(connectorName)),
            API.Services.ChangeLibraryStatus.ConnectorSeriesNotFound => TypedResults.NotFound(nameof(connectorSeriesId)),
            _ => TypedResults.InternalServerError(error ?? "Could not change library")
        };
    }

    /// <summary>
    /// Queues an immediate chapter sync + cover refresh for each of the series' sources (preferring the
    /// enabled ones) — the manual "sync now" trigger.
    /// </summary>
    /// <response code="200"></response>
    /// <response code="404">Series not found</response>
    /// <response code="412">The series has no source links to sync from</response>
    [HttpPost("{SeriesId}/Sync")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status412PreconditionFailed)]
    public async Task<Results<Ok, NotFound<string>, StatusCodeHttpResult>> SyncNow(string SeriesId,
        [FromServices] API.JobRuntime.Interfaces.IJobStore jobStore, [FromServices] API.JobRuntime.Interfaces.IClock clock)
    {
        if (await context.Series.Include(m => m.SourceIds)
                .FirstOrDefaultAsync(m => m.Key == SeriesId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(SeriesId));

        var sources = manga.SourceIds.Where(id => id.UseForDownload).ToList();
        if (sources.Count == 0)
            sources = manga.SourceIds.ToList();
        if (sources.Count == 0)
            return TypedResults.StatusCode(Status412PreconditionFailed);

        foreach (var source in sources)
            await API.JobRuntime.SeriesJobs.EnqueueCoverAndSync(jobStore, clock, source, settings.DownloadLanguage, HttpContext.RequestAborted);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Re-matches a series' source link to a different entry on the same connector — the repair step
    /// when a stored id is wrong and syncs fail or yield nothing. The link is replaced (its key derives
    /// from the site id), the download preference carries over, and a fresh chapter sync is queued.
    /// </summary>
    /// <response code="200">The replacement <see cref="DTOs.SourceId{T}"/></response>
    /// <response code="400">Empty idOnConnectorSite</response>
    /// <response code="404">Series or source link not found, or the link belongs to another series</response>
    [HttpPost("{SeriesId}/Source/{SourceIdKey}/Rematch")]
    [ProducesResponseType<DTOs.SourceId<Series>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<DTOs.SourceId<Series>>, BadRequest<string>, NotFound<string>>> RematchSource(
        string SeriesId, string SourceIdKey, [FromBody] Requests.RematchSourceRecord requestData,
        [FromServices] API.JobRuntime.Interfaces.IJobStore jobStore, [FromServices] API.JobRuntime.Interfaces.IClock clock)
    {
        if (string.IsNullOrWhiteSpace(requestData.IdOnConnectorSite))
            return TypedResults.BadRequest("idOnConnectorSite must not be empty.");

        if (await context.SeriesSourceIds
                .Include(id => id.Obj)
                .FirstOrDefaultAsync(id => id.Key == SourceIdKey, HttpContext.RequestAborted) is not { } oldSource
            || oldSource.ObjId != SeriesId)
            return TypedResults.NotFound(nameof(SourceIdKey));

        var replacement = new Schema.SeriesContext.SourceId<Schema.SeriesContext.Series>(
            oldSource.Obj, oldSource.SeriesSourceName, requestData.IdOnConnectorSite, requestData.WebsiteUrl,
            oldSource.UseForDownload);
        context.SeriesSourceIds.Remove(oldSource);
        context.SeriesSourceIds.Add(replacement);
        if (await context.Sync(HttpContext.RequestAborted, GetType(), "Rematch source") is { success: false } result)
            return TypedResults.NotFound(result.exceptionMessage);

        await API.JobRuntime.SeriesJobs.EnqueueCoverAndSync(jobStore, clock, replacement, settings.DownloadLanguage, HttpContext.RequestAborted);

        return TypedResults.Ok(DTOs.SourceId<Series>.From(replacement));
    }

    /// <summary>
    /// (Un-)Marks <see cref="Series"/> as requested for Download from <see cref="API.Connectors.SeriesSource"/>
    /// </summary>
    /// <param name="SeriesId"><see cref="Series"/> with <paramref name="SeriesId"/></param>
    /// <param name="SeriesSourceName"><see cref="API.Connectors.SeriesSource"/> with <paramref name="SeriesSourceName"/></param>
    /// <param name="IsRequested">true to mark as requested, false to mark as not-requested</param>
    /// <response code="200"></response>
    /// <response code="404"><paramref name="SeriesId"/> or <paramref name="SeriesSourceName"/> not found</response>
    /// <response code="412"><see cref="Series"/> was not linked to <see cref="API.Connectors.SeriesSource"/>, so nothing changed</response>
    /// <response code="428"><see cref="Series"/> is not linked to <see cref="API.Connectors.SeriesSource"/> yet. Search for <see cref="Series"/> on <see cref="API.Connectors.SeriesSource"/> first (to create a <see cref="DTOs.SourceId{T}"/>).</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPatch("{SeriesId}/DownloadFrom/{SeriesSourceName}/{IsRequested}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status404NotFound,  "text/plain")]
    [ProducesResponseType<string>(Status412PreconditionFailed,  "text/plain")]
    [ProducesResponseType<string>(Status428PreconditionRequired,  "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError,  "text/plain")]
    public async Task<Results<Ok, NotFound<string>, StatusCodeHttpResult, InternalServerError<string>>> MarkAsRequested(string SeriesId, string SeriesSourceName, bool IsRequested, [FromServices] API.JobRuntime.Interfaces.IJobStore jobStore, [FromServices] API.JobRuntime.Interfaces.IClock clock)
    {
        if (await context.Series
                .Include(m => m.Chapters)
                .ThenInclude(c => c.SourceIds.Where(chID => chID.SeriesSourceName == SeriesSourceName))
                .Include(m => m.SourceIds.Where(mId => mId.SeriesSourceName == SeriesSourceName))
                .FirstOrDefaultAsync(m => m.Key == SeriesId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(SeriesId));
        if(!connectors.Any(c => c.Name.Equals(SeriesSourceName, StringComparison.InvariantCultureIgnoreCase)))
            return TypedResults.NotFound(nameof(SeriesSourceName));

        if (manga.SourceIds.FirstOrDefault(mId => mId.SeriesSourceName == SeriesSourceName) is not { } mcId)
        {
            if(IsRequested)
                return TypedResults.StatusCode(Status428PreconditionRequired);
            else
                return TypedResults.StatusCode(Status412PreconditionFailed);
        }
        else
        {
            mcId.UseForDownload = IsRequested;
        }

        if (manga.Chapters.SelectMany(ch =>
                ch.SourceIds.Where(chID => chID.SeriesSourceName == SeriesSourceName)) is { } chIds)
        {
            foreach (Schema.SeriesContext.SourceId<Chapter> chId in chIds)
            {
                chId.UseForDownload = IsRequested;
            }
        }

        if(await context.Sync(HttpContext.RequestAborted, GetType(), "Update download from SeriesSource.") is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        await API.JobRuntime.SeriesJobs.EnqueueCoverAndSync(jobStore, clock, mcId, settings.DownloadLanguage, HttpContext.RequestAborted);

        return TypedResults.Ok();
    }
    
    /// <summary>
    /// Initiate a search for <see cref="API.Schema.SeriesContext.Series"/> on a different <see cref="API.Connectors.SeriesSource"/>
    /// </summary>
    /// <param name="SeriesId"><see cref="API.Schema.SeriesContext.Series"/> with <paramref name="SeriesId"/></param>
    /// <param name="SeriesSourceName"><see cref="API.Connectors.SeriesSource"/>.Name</param>
    /// <response code="200"><see cref="MinimalSeries"/> exert of <see cref="Schema.SeriesContext.Series"/></response>
    /// <response code="404"><see cref="API.Connectors.SeriesSource"/> with Name not found</response>
    /// <response code="412"><see cref="API.Connectors.SeriesSource"/> with Name is disabled</response>
    [HttpGet("{SeriesId}/OnSeriesSource/{SeriesSourceName}")]
    [ProducesResponseType<List<MinimalSeries>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status406NotAcceptable)]
    public async Task<Results<Ok<List<MinimalSeries>>, NotFound<string>, StatusCodeHttpResult>> SearchOnDifferentConnector (string SeriesId, string SeriesSourceName)
    {
        if (await context.Series.FirstOrDefaultAsync(m => m.Key == SeriesId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(SeriesId));

        return await new SearchController(context, connectors).SearchSeries(SeriesSourceName, manga.Name);
    }
    
    /// <summary>
    /// Returns all <see cref="Series"/> which where Authored by <see cref="Author"/> with <paramref name="AuthorId"/>
    /// </summary>
    /// <param name="AuthorId"><see cref="Author"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Author"/> with <paramref name="AuthorId"/></response>
    /// /// <response code="500">Error during Database Operation</response>
    [HttpGet("WithAuthorId/{AuthorId}")]
    [ProducesResponseType<List<Series>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<List<Series>>, NotFound<string>, InternalServerError>> GetSeriesWithAuthorIds (string AuthorId)
    {
        if (await context.Authors.FirstOrDefaultAsync(a => a.Key == AuthorId, HttpContext.RequestAborted) is not { } _)
            return TypedResults.NotFound(nameof(AuthorId));

        if (await context.SeriesWithMetadata().Include(m => m.SourceIds)
                .Where(m => m.Authors.Any(a => a.Key == AuthorId))
                .OrderBy(m => m.Name)
                .ToListAsync(HttpContext.RequestAborted) is not { } result)
            return TypedResults.InternalServerError();

        return TypedResults.Ok(result.Select(m =>
        {
            IEnumerable<DTOs.SourceId<Series>> ids = m.SourceIds.Select(id => DTOs.SourceId<Series>.From(id));
            IEnumerable<Author> authors = m.Authors.Select(a => new Author(a.Key, a.AuthorName));
            IEnumerable<string> tags = m.SeriesTags.Select(t => t.Tag);
            IEnumerable<Link> links = m.Links.Select(l => new Link(l.Key, l.LinkProvider, l.LinkUrl));
            IEnumerable<AltTitle> altTitles = m.AltTitles.Select(a => new AltTitle(a.Language, a.Title));
            return new Series(m.Key, m.Name, m.Description, m.ReleaseStatus, ids, m.IgnoreChaptersBefore, m.Year, m.OriginalLanguage, authors, tags, links, altTitles, m.LibraryId);
        }).ToList());
    }
    
    /// <summary>
    /// Returns all <see cref="Series"/> with <see cref="Tag"/>
    /// </summary>
    /// <param name="Tag"><see cref="Tag"/>.Tag</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Tag"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpGet("WithTag/{Tag}")]
    [ProducesResponseType<Series[]>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<List<MinimalSeries>>, NotFound<string>, InternalServerError>> GetSeriesWithTag (string Tag)
    {
        if (await context.Series
                .Include(m => m.SourceIds)
                .Include(m => m.SeriesTags)
                .Where(m => m.SeriesTags.Any(t => t.Tag == Tag))
                .OrderBy(m => m.Name)
                .ToListAsync(HttpContext.RequestAborted) is not { } result)
            return TypedResults.InternalServerError();
        
        return TypedResults.Ok(result.Select(MinimalSeries.From).ToList());
    }

    /// <summary>
    /// Returns <see cref="Schema.SeriesContext.Series"/> with names similar to <see cref="Schema.SeriesContext.Series"/> (identified by <paramref name="SeriesId"/>)
    /// </summary>
    /// <param name="SeriesId">Key of <see cref="Schema.SeriesContext.Series"/></param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Schema.SeriesContext.Series"/> with <paramref name="SeriesId"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpGet("WithSimilarName/{SeriesId}")]
    [ProducesResponseType<List<string>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<List<string>>, NotFound<string>, InternalServerError>> GetSimilarSeries (string SeriesId)
    {
        if (await context.Series.FirstOrDefaultAsync(m => m.Key == SeriesId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(SeriesId));
        
        string name = manga.Name;

        if (await context.Series.Where(m => m.Key != SeriesId)
                .ToDictionaryAsync(m => m.Key, m => m.Name, HttpContext.RequestAborted) is not { } mangaNames)
            return TypedResults.InternalServerError();

        List<string> similarIds = mangaNames
            .Where(kv => NeedlemanWunschStringUtil.CalculateSimilarityPercentage(name, kv.Value) > 0.8)
            .Select(kv => kv.Key)
            .ToList();
        
        return TypedResults.Ok(similarIds);
    }

    /// <summary>
    /// Returns the <see cref="DTOs.SourceId{T}"/> with <see cref="DTOs.SourceId{T}"/>.Key
    /// </summary>
    /// <param name="SeriesSourceIdId">Key of <see cref="DTOs.SourceId{T}"/></param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="DTOs.SourceId{T}"/> with <paramref name="SeriesSourceIdId"/> not found</response>
    [HttpGet("ConnectorId/{SeriesSourceIdId}")]
    [ProducesResponseType<DTOs.SourceId<Series>>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<DTOs.SourceId<Series>>, NotFound<string>>> GetSeriesSourceId (string SeriesSourceIdId)
    {
        if (await context.SeriesSourceIds.FirstOrDefaultAsync(c => c.Key == SeriesSourceIdId, HttpContext.RequestAborted) is not { } mcIdSeries)
            return TypedResults.NotFound(nameof(SeriesSourceIdId));

        DTOs.SourceId<Series> result = new (mcIdSeries.Key, mcIdSeries.SeriesSourceName, mcIdSeries.ObjId, mcIdSeries.IdOnConnectorSite, mcIdSeries.WebsiteUrl, mcIdSeries.UseForDownload);
        
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Force re-check failed/undownloaded <see cref="Chapter"/> for <see cref="Series"/>
    /// </summary>
    /// <param name="seriesId">(optional)<see cref="Series"/>.Key</param>
    /// <response code="200">Affected Records</response>
    [HttpPost("ForceRecheck")]
    [HttpPost("ForceRecheck/{seriesId?}")]
    [ProducesResponseType<int>(Status200OK, "text/plain")]
    public async Task<Ok<int>> ForceRecheckSeriesChapters(string? seriesId = null)
    {
        IQueryable<Schema.SeriesContext.SourceId<Chapter>> queryable = context.ChapterSourceIds.Where(chId  => chId.Obj!.Downloaded);
        if(seriesId is not null)
            queryable = queryable.Where(chId => chId.Obj!.ParentSeriesId == seriesId);
        
        int rowsAffected = await queryable.ExecuteDeleteAsync(HttpContext.RequestAborted);

        return TypedResults.Ok(rowsAffected);
    }

    /// <summary>
    /// Force re-check a specific <see cref="Chapter"/> by deleting its record.
    /// </summary>
    /// <param name="chapterId"><see cref="Chapter"/>.Key</param>
    /// <response code="200">Affected records</response>
    [HttpPost("ForceRecheck/Chapter/{chapterId}")]
    [ProducesResponseType<int>(Status200OK, "text/plain")]
    public async Task<Ok<int>> ForceRecheckChapter(string chapterId)
    {
        IQueryable<Schema.SeriesContext.SourceId<Chapter>> queryable = context.ChapterSourceIds.Where(chId  => chId.ObjId == chapterId);
        
        int rowsAffected = await queryable.ExecuteDeleteAsync(HttpContext.RequestAborted);

        return TypedResults.Ok(rowsAffected);
    }
}