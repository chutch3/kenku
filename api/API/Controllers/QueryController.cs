using API.Controllers.DTOs;
using API.Schema.ActionsContext;
using API.Schema.NotificationsContext;
using API.Schema.SeriesContext;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;
using Author = API.Controllers.DTOs.Author;

// ReSharper disable InconsistentNaming

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/")]
public class QueryController(SeriesContext mangaContext, NotificationsContext notificationsContext, ActionsContext actionsContext)
    : ControllerBase
{
    /// <summary>
    /// Returns the <see cref="Author"/> with <paramref name="AuthorId"/>
    /// </summary>
    /// <param name="AuthorId"><see cref="Author"/>.Key</param>
    /// <response code="200"></response>
    /// <response code="404"><see cref="Author"/> with <paramref name="AuthorId"/> not found</response>
    [HttpGet("Author/{AuthorId}")]
    [ProducesResponseType<Author>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<Author>, NotFound<string>>> GetAuthor (string AuthorId)
    {
        if (await mangaContext.Authors.FirstOrDefaultAsync(a => a.Key == AuthorId, HttpContext.RequestAborted) is not { } author)
            return TypedResults.NotFound(nameof(AuthorId));
        
        return TypedResults.Ok(new Author(author.Key, author.AuthorName));
    }

    /// <summary>
    /// Returns the Server-Stats
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("Stats")]
    [ProducesResponseType<Stats>(Status200OK, "application/json")]
    public async Task<Ok<Stats>> GetStats()
    {
        // Counted via EF per context — the tables live in different DbContexts (and a raw cross-table
        // query also hardcoded the wrong physical table name for Series, which is mapped to "Mangas").
        CancellationToken ct = HttpContext.RequestAborted;
        Stats stats = new(
            NumberManga: await mangaContext.Series.CountAsync(ct),
            NumberChapters: await mangaContext.Chapters.CountAsync(ct),
            MissingChapters: await mangaContext.Chapters.CountAsync(c => !c.Downloaded, ct),
            DownloadedChapters: await mangaContext.Chapters.CountAsync(c => c.Downloaded, ct),
            SentNotifications: await notificationsContext.Notifications.CountAsync(n => n.IsSent, ct),
            ActionsTaken: await actionsContext.Actions.CountAsync(ct),
            NumberAuthors: await mangaContext.Authors.CountAsync(ct),
            NumberTags: await mangaContext.Tags.CountAsync(ct));
        return TypedResults.Ok(stats);
    }
}