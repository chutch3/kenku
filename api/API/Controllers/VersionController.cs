using API.Controllers.Responses;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class VersionController : ControllerBase
{
    /// <summary>
    /// The running build's identity: release tag (or branch@commit for untagged builds), commit, build time.
    /// </summary>
    /// <response code="200"></response>
    [HttpGet]
    [ProducesResponseType<VersionResponse>(Status200OK, "application/json")]
    public Ok<VersionResponse> GetVersion() => TypedResults.Ok(VersionResponse.Current);
}
