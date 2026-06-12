using API.Controllers.Responses;
using API.DownloadClients.Interfaces;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace API.Controllers;

/// <summary>
/// In-flight torrent visibility: what the download client currently holds in Kenku's category.
/// The client is resolved optionally — it is only registered when a download client is configured,
/// and deployments without one simply see an empty list.
/// </summary>
[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class TorrentsController : ControllerBase
{
    /// <summary>Downloads currently in the torrent client, with live progress and seeders.</summary>
    /// <response code="200"></response>
    [HttpGet]
    [ProducesResponseType<List<TorrentResponse>>(Status200OK, "application/json")]
    public async Task<Ok<List<TorrentResponse>>> GetAll()
    {
        if (HttpContext.RequestServices.GetService(typeof(IDownloadClient)) is not IDownloadClient client)
            return TypedResults.Ok(new List<TorrentResponse>());

        var entries = await client.List(HttpContext.RequestAborted);
        return TypedResults.Ok(entries.Select(TorrentResponse.From).ToList());
    }
}
