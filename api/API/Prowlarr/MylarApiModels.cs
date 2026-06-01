using Newtonsoft.Json;

namespace API.Prowlarr;

// The Mylar-emulating endpoint's responses are serialized by the MVC pipeline, which is configured
// for Newtonsoft (AddNewtonsoftJson in Program.cs). Prowlarr keys off the exact lowercase wire names
// (`success`/`data`/`torznabs`/`name`/...), so each property carries an explicit Newtonsoft
// [JsonProperty] name rather than relying on a global naming policy.

/// <summary>
/// Generic status response (getVersion / addProvider / changeProvider / delProvider).
/// </summary>
public record MylarStatusResponse(
    [property: JsonProperty("success")] bool Success,
    [property: JsonProperty("data")] object? Data,
    [property: JsonProperty("error")] object? Error);

/// <summary>
/// Response for <c>listProviders</c>.
/// </summary>
public record MylarListResponse(
    [property: JsonProperty("success")] bool Success,
    [property: JsonProperty("data")] MylarIndexerData Data,
    [property: JsonProperty("error")] object? Error);

public record MylarIndexerData(
    [property: JsonProperty("torznabs")] List<MylarIndexer> Torznabs,
    [property: JsonProperty("newznabs")] List<MylarIndexer> Newznabs);

public record MylarIndexer(
    [property: JsonProperty("name")] string Name,
    [property: JsonProperty("host")] string Host,
    [property: JsonProperty("apikey")] string Apikey,
    [property: JsonProperty("categories")] string Categories,
    [property: JsonProperty("enabled")] bool Enabled,
    [property: JsonProperty("altername")] string Altername);
