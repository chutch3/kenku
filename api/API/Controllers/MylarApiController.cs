using API.Auth;
using API.Prowlarr;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Emulates Mylar3's Prowlarr-facing application contract so Prowlarr can sync (push)
/// indexers into Kenku. All commands are GET to <c>/api</c> dispatched by the
/// <c>cmd</c> query parameter and authenticated by the <c>apikey</c> query parameter.
/// Hidden from the versioned OpenAPI document / web client.
/// </summary>
[ApiController]
[ApiVersionNeutral]
[ApiExplorerSettings(IgnoreApi = true)]
[RequireApiKey]
[Route("api")]
public class MylarApiController : ControllerBase
{
    private readonly KenkuSettings _settings;

    public MylarApiController(KenkuSettings settings)
    {
        _settings = settings;
    }

    // NOTE: addProvider/changeProvider/delProvider mutate state on an HTTP GET. This violates the
    // "GET is safe" convention but is REQUIRED by Mylar's protocol — Prowlarr really does issue these
    // as GETs with query params. Do not "fix" this into POST/PUT/DELETE; that would break Prowlarr
    // sync. The apikey query-param check (RequireApiKey) is the access control for these mutations.
    [HttpGet]
    public IActionResult Dispatch([FromQuery] string? cmd)
    {
        return (cmd ?? string.Empty).ToLowerInvariant() switch
        {
            "getversion" => GetVersion(),
            "listproviders" => ListProviders(),
            "addprovider" => AddOrChangeProvider(),
            "changeprovider" => AddOrChangeProvider(),
            "delprovider" => DelProvider(),
            _ => BadRequest(new MylarStatusResponse(false, null, new { message = $"Unknown command '{cmd}'." })),
        };
    }

    private IActionResult GetVersion()
        => Ok(new MylarStatusResponse(true, "kenku", null));

    private IActionResult ListProviders()
    {
        var indexers = _settings.SnapshotSyncedIndexers();
        var torznabs = indexers
            .Where(i => !string.Equals(i.Protocol, MylarIndexerMapping.ProtocolUsenet, StringComparison.OrdinalIgnoreCase))
            .Select(MylarIndexerMapping.ToMylarIndexer)
            .ToList();
        var newznabs = indexers
            .Where(i => string.Equals(i.Protocol, MylarIndexerMapping.ProtocolUsenet, StringComparison.OrdinalIgnoreCase))
            .Select(MylarIndexerMapping.ToMylarIndexer)
            .ToList();
        return Ok(new MylarListResponse(true, new MylarIndexerData(torznabs, newznabs), null));
    }

    private IActionResult AddOrChangeProvider()
    {
        var q = Request.Query;
        var name = q["name"].ToString();
        var providerType = q["providertype"].ToString();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new MylarStatusResponse(false, null, new { message = "name is required." }));

        var protocol = MylarIndexerMapping.ProviderTypeToProtocol(providerType);
        var host = q["host"].ToString();
        var apiKey = q["prov_apikey"].ToString();
        var categories = MylarIndexerMapping.ParseCategories(q["categories"].ToString());
        var enabled = ParseBool(q["enabled"].ToString(), defaultValue: true);

        _settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(
            0, name, host, apiKey, categories, protocol, enabled));

        return Ok(new MylarStatusResponse(true, null, null));
    }

    private IActionResult DelProvider()
    {
        var q = Request.Query;
        var name = q["name"].ToString();
        var protocol = MylarIndexerMapping.ProviderTypeToProtocol(q["providertype"].ToString());
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new MylarStatusResponse(false, null, new { message = "name is required." }));

        _settings.RemoveSyncedIndexer(name, protocol);
        return Ok(new MylarStatusResponse(true, null, null));
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        if (bool.TryParse(value, out var b))
            return b;
        return value is "1" or "yes" or "on";
    }
}
