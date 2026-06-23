using log4net;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.SeriesContext;
using API.Schema.SeriesContext.MetadataFetchers;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;
// ReSharper disable InconsistentNaming

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class MetadataFetcherController(
    SeriesContext mangaContext, ActionsContext actionsContext, IEnumerable<MetadataFetcher> fetchers) : ControllerBase
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(MetadataFetcherController));
    /// <summary>
    /// Get all <see cref="MetadataFetcher"/> (Metadata-Sites)
    /// </summary>
    /// <response code="200">Names of <see cref="MetadataFetcher"/> (Metadata-Sites)</response>
    [HttpGet]
    [ProducesResponseType<List<string>>(Status200OK, "application/json")]
    public Ok<List<string>> GetConnectors ()
    {
        return TypedResults.Ok(fetchers.Select(m => m.Name).ToList());
    }

    /// <summary>
    /// Returns all <see cref="MetadataEntry"/>
    /// </summary>
    /// <response code="200"></response>
    /// <response code="500">Error during Database Operation</response>
    [HttpGet("Links")]
    [ProducesResponseType<List<MetadataEntry>>(Status200OK, "application/json")]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<List<MetadataEntry>>, InternalServerError>> GetLinkedEntries ()
    {
        if (await mangaContext.MetadataEntries.ToListAsync(HttpContext.RequestAborted) is not { } result)
            return TypedResults.InternalServerError();
        
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Returns all <see cref="MetadataEntry"/> for <see cref="Series"/> with <paramref name="SeriesId"/>
    /// </summary>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Series"/> with <paramref name="SeriesId"/> not found</response>
    [HttpGet("Links/{SeriesId}")]
    [ProducesResponseType<List<MetadataEntry>>(Status200OK, "application/json")]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok<List<MetadataEntry>>, NotFound<string>>> GetLinkedEntries(string SeriesId)
    {
        if (await mangaContext.MetadataEntries.Where(me => me.SeriesId == SeriesId).ToListAsync(HttpContext.RequestAborted) is not { } result)
            return TypedResults.NotFound(nameof(SeriesId));
        
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Searches <see cref="MetadataFetcher"/> (Metadata-Sites) for Series-Metadata
    /// </summary>
    /// <param name="SeriesId"><see cref="Series"/>.Key</param>
    /// <param name="MetadataFetcherName"><see cref="MetadataFetcher"/>.Name</param>
    /// <param name="searchTerm">Instead of using the <paramref name="SeriesId"/> for search on Website, use a specific term</param>
    /// <response code="200"></response>
    /// <response code="400"><see cref="MetadataFetcher"/> (Metadata-Sites) with <paramref name="MetadataFetcherName"/> does not exist</response>
    /// <response code="404"><see cref="Series"/> with <paramref name="SeriesId"/> not found</response>
    [HttpPost("{MetadataFetcherName}/SearchManga/{SeriesId}")]
    [ProducesResponseType<MetadataSearchResult[]>(Status200OK, "application/json")]
    [ProducesResponseType(Status400BadRequest)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<List<MetadataSearchResult>>, BadRequest, NotFound<string>, InternalServerError<string>>> SearchMangaMetadata(string SeriesId, string MetadataFetcherName, [FromBody (EmptyBodyBehavior = EmptyBodyBehavior.Allow)]string? searchTerm = null)
    {
        if (await mangaContext.Series.FirstOrDefaultAsync(m => m.Key == SeriesId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(SeriesId));
        if(fetchers.FirstOrDefault(f => f.Name == MetadataFetcherName) is not { } fetcher)
            return TypedResults.BadRequest();

        try
        {
            MetadataSearchResult[] searchResults = searchTerm is null ? await fetcher.SearchMetadataEntry(manga) : await fetcher.SearchMetadataEntry(searchTerm);
            return TypedResults.Ok(searchResults.ToList());
        }
        catch (Exception e)
        {
            Log.Error($"Error searching metadata with {MetadataFetcherName}", e);
            return TypedResults.InternalServerError(e.Message);
        }
    }

    /// <summary>
    /// Links <see cref="MetadataFetcher"/> (Metadata-Sites) using Provider-Specific Identifier to <see cref="Series"/>
    /// </summary>
    /// <param name="SeriesId"><see cref="Series"/>.Key</param>
    /// <param name="MetadataFetcherName"><see cref="MetadataFetcher"/>.Name</param>
    /// <param name="Identifier"><see cref="MetadataFetcherName"/>-Specific ID</param>
    /// <response code="200"></response>
    /// <response code="400"><see cref="MetadataFetcher"/> (Metadata-Sites) with <paramref name="MetadataFetcherName"/> does not exist</response>
    /// <response code="404"><see cref="Series"/> with <paramref name="SeriesId"/> not found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPost("{MetadataFetcherName}/Link/{SeriesId}")]
    [ProducesResponseType<MetadataEntry>(Status200OK, "application/json")]
    [ProducesResponseType(Status400BadRequest)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok, BadRequest, NotFound<string>, InternalServerError<string>>> LinkMangaMetadata (string SeriesId, string MetadataFetcherName, [FromBody]string Identifier)
    {
        if (await mangaContext.Series.FirstOrDefaultAsync(m => m.Key == SeriesId, HttpContext.RequestAborted) is not { } manga)
            return TypedResults.NotFound(nameof(SeriesId));
        if(fetchers.FirstOrDefault(f => f.Name == MetadataFetcherName) is not { } fetcher)
            return TypedResults.BadRequest();
        
        MetadataEntry entry = fetcher.CreateMetadataEntry(manga, Identifier);
        mangaContext.MetadataEntries.Add(entry);
        
        if(await mangaContext.Sync(HttpContext.RequestAborted, GetType(), "Link Metadatafetcher") is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);

        await fetcher.UpdateMetadata(entry, mangaContext, HttpContext.RequestAborted);
        
        return TypedResults.Ok();
    }

    /// <summary>
    /// Un-Links <see cref="MetadataFetcher"/> (Metadata-Sites) from <see cref="Series"/>
    /// </summary>
    /// <response code="200"></response>
    /// <response code="400"><see cref="MetadataFetcher"/> (Metadata-Sites) with <paramref name="MetadataFetcherName"/> does not exist</response>
    /// <response code="404"><see cref="Series"/> with <paramref name="SeriesId"/> not found</response>
    /// <response code="412">No <see cref="MetadataEntry"/> linking <see cref="Series"/> and <see cref="MetadataFetcher"/> found</response>
    /// <response code="500">Error during Database Operation</response>
    [HttpPost("{MetadataFetcherName}/Unlink/{SeriesId}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType(Status400BadRequest)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status412PreconditionFailed)]
    [ProducesResponseType<string>(Status500InternalServerError, "text/plain")]
    public async Task<Results<Ok, BadRequest, NotFound<string>, InternalServerError<string>, StatusCodeHttpResult>> UnlinkMangaMetadata (string SeriesId, string MetadataFetcherName)
    {
        if (!await mangaContext.Series.AnyAsync(m => m.Key == SeriesId, HttpContext.RequestAborted))
            return TypedResults.NotFound(nameof(SeriesId));
        if(fetchers.All(f => f.Name != MetadataFetcherName))
            return TypedResults.BadRequest();
        if (await mangaContext.MetadataEntries.Where(e => e.SeriesId == SeriesId && e.MetadataFetcherName == MetadataFetcherName)
                .ExecuteDeleteAsync(HttpContext.RequestAborted) < 1)
            return TypedResults.StatusCode(Status412PreconditionFailed);
        
        if(await mangaContext.Sync(HttpContext.RequestAborted, GetType(), "Unlink Metadatafetcher") is { success: false } result)
            return TypedResults.InternalServerError(result.exceptionMessage);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Updates the Metadata of <see cref="Series"/> with <paramref name="SeriesId"/>
    /// </summary>
    /// <response code="200">Metadata updated</response>
    /// <response code="400"><see cref="MetadataFetcher"/> not found</response>
    /// <response code="412">No <see cref="MetadataEntry"/> linking <see cref="Series"/> and <see cref="MetadataFetcher"/> found</response>
    [HttpPost("{MetadataFetcherName}/Update/{SeriesId}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType(Status400BadRequest)]
    [ProducesResponseType(Status412PreconditionFailed)]
    public async Task<Results<Ok, StatusCodeHttpResult, BadRequest>> UpdateMetadata(string SeriesId, string MetadataFetcherName)
    {
        if (await mangaContext.MetadataEntries.FirstOrDefaultAsync(e => e.MetadataFetcherName == MetadataFetcherName && e.SeriesId == SeriesId, HttpContext.RequestAborted) is not { } metadataEntry)
            return TypedResults.StatusCode(Status412PreconditionFailed);
        
        if(fetchers.FirstOrDefault(f => f.Name == metadataEntry.MetadataFetcherName) is not { } fetcher)
            return TypedResults.BadRequest();

        await fetcher.UpdateMetadata(metadataEntry, mangaContext, HttpContext.RequestAborted);
        actionsContext.Actions.Add(new MetadataUpdatedActionRecord(metadataEntry.Series, fetcher));
        return TypedResults.Ok();
    }
}