using API.Controllers.Requests;
using API.Controllers.Responses;
using API.HttpRequesters;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.StatusCodes;
// ReSharper disable InconsistentNaming

namespace API.Controllers;

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class SettingsController(KenkuSettings settings) : ControllerBase
{
    /// <summary>
    /// Get the current settings (secret-free projection — credentials are never returned).
    /// </summary>
    /// <response code="200"></response>
    [HttpGet]
    [ProducesResponseType<SettingsResponse>(Status200OK, "application/json")]
    public Ok<SettingsResponse> GetSettings([FromServices] API.Indexers.IndexerCooldown cooldowns)
    {
        return TypedResults.Ok(SettingsResponse.From(settings, cooldowns));
    }
    
    /// <summary>
    /// Get the current UserAgent used by Kenku
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("UserAgent")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    public Ok<string> GetUserAgent()
    {
        return TypedResults.Ok(settings.UserAgent);
    }
    
    /// <summary>
    /// Set a new UserAgent
    /// </summary>
    /// <response code="200"></response>
    [HttpPatch("UserAgent")]
    [ProducesResponseType(Status200OK)]
    public Ok SetUserAgent([FromBody]string userAgent)
    {
        //TODO Validate
        settings.SetUserAgent(userAgent);
        return TypedResults.Ok();
    }
    
    /// <summary>
    /// Reset the UserAgent to default
    /// </summary>
    /// <response code="200"></response>
    [HttpDelete("UserAgent")]
    [ProducesResponseType(Status200OK)]
    public Ok ResetUserAgent()
    {
        settings.SetUserAgent(KenkuSettings.DefaultUserAgent);
        return TypedResults.Ok();
    }
    
    /// <summary>
    /// Returns Level of Image-Compression for Images
    /// </summary>
    /// <response code="200">JPEG ImageCompression-level as Integer</response>
    [HttpGet("ImageCompressionLevel")]
    [ProducesResponseType<int>(Status200OK, "text/plain")]
    public Ok<int> GetImageCompression()
    {
        return TypedResults.Ok(settings.ImageCompression);
    }
    
    /// <summary>
    /// Set the Image-Compression-Level for Images
    /// </summary>
    /// <param name="level">100 to disable, 0-99 for JPEG ImageCompression-Level</param>
    /// <response code="200"></response>
    /// <response code="400">Level outside permitted range</response>
    [HttpPatch("ImageCompressionLevel/{level}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType(Status400BadRequest)]
    public Results<Ok, BadRequest> SetImageCompression(int level)
    {
        if (level < 1 || level > 100)
            return TypedResults.BadRequest();
        settings.UpdateImageCompression(level);
        return TypedResults.Ok();
    }
    
    /// <summary>
    /// Get state of Black/White-Image setting
    /// </summary>
    /// <response code="200">True if enabled</response>
    [HttpGet("BWImages")]
    [ProducesResponseType<bool>(Status200OK, "text/plain")]
    public Ok<bool> GetBwImagesToggle()
    {
        return TypedResults.Ok(settings.BlackWhiteImages);
    }
    
    /// <summary>
    /// Enable/Disable conversion of Images to Black and White
    /// </summary>
    /// <param name="enabled">true to enable</param>
    /// <response code="200"></response>
    [HttpPatch("BWImages/{enabled}")]
    [ProducesResponseType(Status200OK)]
    public Ok SetBwImagesToggle(bool enabled)
    {
        settings.SetBlackWhiteImageEnabled(enabled);
        return TypedResults.Ok();
    }
    
    /// <summary>
    /// Gets the Chapter Naming Scheme
    /// </summary>
    /// <remarks>
    /// Placeholders:
    /// %M Obj Name
    /// %V Volume
    /// %C Chapter
    /// %T Title
    /// %A Author (first in list)
    /// %I Chapter Internal ID
    /// %i Obj Internal ID
    /// %Y Year (Obj)
    ///
    /// ?_(...) replace _ with a value from above:
    /// Everything inside the braces will only be added if the value of %_ is not null
    /// </remarks>
    /// <response code="200"></response>
    [HttpGet("ChapterNamingScheme")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    public Ok<string> GetCustomNamingScheme()
    {
        return TypedResults.Ok(settings.ChapterNamingScheme);
    }
    
    /// <summary>
    /// Sets the Chapter Naming Scheme
    /// </summary>
    /// <remarks>
    /// Placeholders:
    /// %M Obj Name
    /// %V Volume
    /// %C Chapter
    /// %T Title
    /// %A Author (first in list)
    /// %Y Year (Obj)
    ///
    /// ?_(...) replace _ with a value from above:
    /// Everything inside the braces will only be added if the value of %_ is not null
    /// </remarks>
    /// <response code="200"></response>
    [HttpPatch("ChapterNamingScheme")]
    [ProducesResponseType(Status200OK)]
    public Ok SetCustomNamingScheme([FromBody]string namingScheme)
    {
        //TODO Move old Chapters
        settings.SetChapterNamingScheme(namingScheme);
        
        return TypedResults.Ok();
    }

    /// <summary>
    /// Sets the FlareSolverr-URL
    /// </summary>
    /// <param name="flareSolverrUrl">URL of FlareSolverr-Instance</param>
    /// <response code="200"></response>
    [HttpPatch("FlareSolverr/Url")]
    [ProducesResponseType(Status200OK)]
    public Ok SetFlareSolverrUrl([FromBody]string flareSolverrUrl)
    {
        settings.SetFlareSolverrUrl(flareSolverrUrl);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Resets the FlareSolverr-URL (HttpClient does not use FlareSolverr anymore)
    /// </summary>
    /// <response code="200"></response>
    [HttpDelete("FlareSolverr/Url")]
    [ProducesResponseType(Status200OK)]
    public Ok ClearFlareSolverrUrl()
    {
        settings.SetFlareSolverrUrl(string.Empty);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Test FlareSolverr
    /// </summary>
    /// <response code="200">FlareSolverr is working!</response>
    /// <response code="500">FlareSolverr is not working</response>
    [HttpPost("FlareSolverr/Test")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType(Status500InternalServerError)]
    public async Task<Results<Ok, InternalServerError>> TestFlareSolverrReachable()
    {
        const string knownProtectedUrl = "https://prowlarr.servarr.com/v1/ping";
        FlareSolverrRequester client = new(new HttpClient(), settings);
        HttpResponseMessage result = await client.MakeRequest(knownProtectedUrl, RequestType.Default);
        return result.IsSuccessStatusCode ? TypedResults.Ok() : TypedResults.InternalServerError(); 
    }

    /// <summary>
    /// Returns the language in which Series are downloaded
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("DownloadLanguage")]
    [ProducesResponseType<string>(Status200OK,  "text/plain")]
    public Ok<string> GetDownloadLanguage()
    {
        return TypedResults.Ok(settings.DownloadLanguage);
    }

    /// <summary>
    /// Sets the language in which Series are downloaded
    /// </summary>
    /// <response code="200"></response>
    [HttpPatch("DownloadLanguage/{Language}")]
    [ProducesResponseType(Status200OK)]
    public Ok SetDownloadLanguage(string Language)
    {
        //TODO Validation
        settings.SetDownloadLanguage(Language);
        return TypedResults.Ok();
    }
    

    /// <summary>
    /// Sets the time when Libraries are refreshed
    /// </summary>
    /// <response code="200"></response>
    [HttpPatch("LibraryRefresh")]
    [ProducesResponseType(Status200OK)]
    public Ok SetLibraryRefresh([FromBody]PatchLibraryRefreshRecord requestData)
    {
        settings.SetLibraryRefreshSetting(requestData.Setting);
        if(requestData.RefreshLibraryWhileDownloadingEveryMinutes is { } value)
            settings.SetRefreshLibraryWhileDownloadingEveryMinutes(value);
        return TypedResults.Ok();
    }

    /// <summary>
    /// How many times a chapter download is attempted before it parks in NeedsAttention (the retry budget).
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("DownloadMaxAttempts")]
    [ProducesResponseType<int>(Status200OK, "application/json")]
    public Ok<int> GetDownloadMaxAttempts() => TypedResults.Ok(settings.DownloadMaxAttempts);

    /// <summary>
    /// Sets the chapter-download retry budget.
    /// </summary>
    /// <response code="200"></response>
    /// <response code="400">Must be at least 1</response>
    [HttpPatch("DownloadMaxAttempts/{attempts}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public Results<Ok, BadRequest<string>> SetDownloadMaxAttempts(int attempts)
    {
        if (attempts < 1)
            return TypedResults.BadRequest("Retry attempts must be at least 1.");
        settings.SetDownloadMaxAttempts(attempts);
        return TypedResults.Ok();
    }

    /// <summary>
    /// How long Succeeded/Cancelled jobs stay in the queue before the cleanup job prunes them.
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("CompletedJobRetentionDays")]
    [ProducesResponseType<int>(Status200OK, "application/json")]
    public Ok<int> GetCompletedJobRetentionDays() => TypedResults.Ok(settings.CompletedJobRetentionDays);

    /// <summary>
    /// Sets the completed-job retention window in days.
    /// </summary>
    /// <response code="200"></response>
    /// <response code="400">Must be at least 1 day</response>
    [HttpPatch("CompletedJobRetentionDays/{days}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public Results<Ok, BadRequest<string>> SetCompletedJobRetentionDays(int days)
    {
        if (days < 1)
            return TypedResults.BadRequest("Retention must be at least 1 day.");
        settings.SetCompletedJobRetentionDays(days);
        return TypedResults.Ok();
    }

    /// <summary>
    /// How torrent releases are picked for comics: seeder floor, preferred and blocked filename tokens.
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("ReleaseSelection")]
    [ProducesResponseType<ReleaseSelectionRecord>(Status200OK, "application/json")]
    public Ok<ReleaseSelectionRecord> GetReleaseSelection() =>
        TypedResults.Ok(new ReleaseSelectionRecord(settings.ReleaseMinSeeders, settings.ReleasePreferredTokens, settings.ReleaseBlockedTokens));

    /// <summary>
    /// Updates the torrent release-selection rules.
    /// </summary>
    /// <response code="200"></response>
    /// <response code="400">MinSeeders must not be negative</response>
    [HttpPatch("ReleaseSelection")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public Results<Ok, BadRequest<string>> SetReleaseSelection([FromBody] ReleaseSelectionRecord requestData)
    {
        if (requestData.MinSeeders < 0)
            return TypedResults.BadRequest("MinSeeders must not be negative.");
        settings.SetReleaseSelection(requestData.MinSeeders, requestData.PreferredTokens, requestData.BlockedTokens);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Sets Metron (metron.cloud) metadata credentials
    /// </summary>
    /// <response code="200"></response>
    [HttpPatch("Metron")]
    [ProducesResponseType(Status200OK)]
    public Ok SetMetron([FromBody]SetMetronRecord requestData)
    {
        settings.SetMetronCredentials(requestData.Username, requestData.Password);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Clears Metron credentials (disables Metron lookups)
    /// </summary>
    /// <response code="200"></response>
    [HttpDelete("Metron")]
    [ProducesResponseType(Status200OK)]
    public Ok ClearMetron()
    {
        settings.SetMetronCredentials(string.Empty, string.Empty);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Returns the API key Prowlarr uses to push indexers into Kenku.
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("ApiKey")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    public Ok<string> GetApiKey()
    {
        return TypedResults.Ok(settings.ApiKey);
    }

    /// <summary>
    /// Regenerates the API key Prowlarr uses to push indexers into Kenku.
    /// </summary>
    /// <response code="200"></response>
    [HttpPost("ApiKey/Regenerate")]
    [ProducesResponseType<string>(Status200OK, "text/plain")]
    public Ok<string> RegenerateApiKey()
    {
        settings.RegenerateApiKey();
        return TypedResults.Ok(settings.ApiKey);
    }

    /// <summary>
    /// Lists the configured download clients.
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("DownloadClients")]
    [ProducesResponseType<List<DownloadClientResponse>>(Status200OK, "application/json")]
    public Ok<List<DownloadClientResponse>> GetDownloadClients()
    {
        return TypedResults.Ok(settings.SnapshotDownloadClients().Select(ToResponse).ToList());
    }

    /// <summary>
    /// Adds a download client.
    /// </summary>
    /// <response code="200"></response>
    /// <response code="400">Name and base URL are required</response>
    [HttpPost("DownloadClients")]
    [ProducesResponseType<DownloadClientResponse>(Status200OK, "application/json")]
    [ProducesResponseType(Status400BadRequest)]
    public Results<Ok<DownloadClientResponse>, BadRequest> AddDownloadClient([FromBody]SetDownloadClientRecord requestData)
    {
        if (string.IsNullOrWhiteSpace(requestData.Name) || string.IsNullOrWhiteSpace(requestData.BaseUrl))
            return TypedResults.BadRequest();
        int id = settings.AddDownloadClient(ToConfig(requestData));
        return TypedResults.Ok(ToResponse(settings.SnapshotDownloadClients().First(c => c.Id == id)));
    }

    /// <summary>
    /// Updates an existing download client.
    /// </summary>
    /// <response code="200"></response>
    /// <response code="400">Name and base URL are required</response>
    /// <response code="404">No download client with that id</response>
    [HttpPut("DownloadClients")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType(Status400BadRequest)]
    [ProducesResponseType(Status404NotFound)]
    public Results<Ok, BadRequest, NotFound> UpdateDownloadClient([FromBody]SetDownloadClientRecord requestData)
    {
        if (string.IsNullOrWhiteSpace(requestData.Name) || string.IsNullOrWhiteSpace(requestData.BaseUrl))
            return TypedResults.BadRequest();
        return settings.UpdateDownloadClient(ToConfig(requestData))
            ? TypedResults.Ok()
            : TypedResults.NotFound();
    }

    /// <summary>
    /// Removes a download client.
    /// </summary>
    /// <response code="200"></response>
    /// <response code="404">No download client with that id</response>
    [HttpDelete("DownloadClients/{id}")]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType(Status404NotFound)]
    public Results<Ok, NotFound> RemoveDownloadClient(int id)
    {
        return settings.RemoveDownloadClient(id) ? TypedResults.Ok() : TypedResults.NotFound();
    }

    private static DownloadClientConfig ToConfig(SetDownloadClientRecord r) =>
        new(r.Id, r.Name, r.Type, r.BaseUrl, r.Username, r.Password, r.Category, r.Enabled, r.Priority);

    private static DownloadClientResponse ToResponse(DownloadClientConfig c) =>
        new(c.Id, c.Name, c.Type, c.BaseUrl, c.Username, c.Category, c.Enabled, c.Priority);
}