namespace API.Prowlarr;

/// <summary>
/// Translates between Kenku's <see cref="SyncedIndexerConfig"/> and the Mylar wire shape Prowlarr
/// pushes: provider type (Torznab/Newznab) ↔ protocol (torrent/usenet), and the comma-separated
/// category string ↔ int array.
/// </summary>
public static class MylarIndexerMapping
{
    public const string ProtocolTorrent = "torrent";
    public const string ProtocolUsenet = "usenet";

    public static int[] ParseCategories(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return Array.Empty<int>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .ToArray();
    }

    public static string FormatCategories(IEnumerable<int> categories)
        => string.Join(",", categories);

    public static string ProviderTypeToProtocol(string? providerType)
        => string.Equals(providerType, "Newznab", StringComparison.OrdinalIgnoreCase)
            ? ProtocolUsenet
            : ProtocolTorrent;

    public static string ProtocolToProviderType(string? protocol)
        => string.Equals(protocol, ProtocolUsenet, StringComparison.OrdinalIgnoreCase)
            ? "Newznab"
            : "Torznab";

    public static MylarIndexer ToMylarIndexer(SyncedIndexerConfig config)
        => new(
            config.Name,
            config.Url,
            config.ApiKey,
            FormatCategories(config.Categories),
            config.Enabled,
            config.Name);
}
