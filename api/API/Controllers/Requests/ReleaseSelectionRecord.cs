using System.ComponentModel;

namespace API.Controllers.Requests;

/// <summary>
/// How torrent releases are picked for comics (v1 scoring — not arr-style profiles). Descriptions sit
/// on the constructor parameters, not redefined properties: ASP.NET model validation rejects a record
/// that carries validation/binding metadata on a property that is also a primary-ctor parameter.
/// </summary>
public record ReleaseSelectionRecord(
    [property: Description("Releases with fewer seeders are never picked.")] int MinSeeders,
    [property: Description("Filename tokens that rank a release higher (e.g. cbz).")] string[] PreferredTokens,
    [property: Description("Filename tokens that exclude a release outright (e.g. cbr, pdf).")] string[] BlockedTokens);
