namespace API.Discovery;

/// <summary>One Discover rail with its entries — the unit returned by the data-driven
/// <c>GET /v2/Discover/Rails</c> endpoint. The frontend renders these generically, splitting on
/// <see cref="ContentType"/>.</summary>
public record DiscoveryRailResponse(string Id, string Label, API.Connectors.ContentType ContentType, List<DiscoveryEntry> Entries);
