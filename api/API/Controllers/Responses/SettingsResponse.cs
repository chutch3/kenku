namespace API.Controllers.Responses;

/// <summary>
/// Secret-free projection of <see cref="KenkuSettings"/> returned by <c>GET /v2/Settings</c>.
/// Credentials (download-client passwords, indexer API keys, Metron password) are deliberately
/// omitted — the persistence model keeps them, the API contract does not. <see cref="ApiKey"/> (the
/// Prowlarr push key) is intentionally included because the UI displays it for copying.
/// </summary>
public record SettingsResponse(
    string ApiKey,
    bool MetronConfigured,
    IReadOnlyList<SyncedIndexerResponse> SyncedIndexers,
    IReadOnlyList<DownloadClientResponse> DownloadClients)
{
    public static SettingsResponse From(KenkuSettings s) => new(
        s.ApiKey,
        !string.IsNullOrWhiteSpace(s.MetronUsername),
        s.SnapshotSyncedIndexers()
            .Select(i => new SyncedIndexerResponse(i.Id, i.Name, i.Url, i.Categories, i.Protocol, i.Enabled))
            .ToList(),
        s.SnapshotDownloadClients()
            .Select(c => new DownloadClientResponse(c.Id, c.Name, c.Type, c.BaseUrl, c.Username, c.Category, c.Enabled, c.Priority))
            .ToList());
}

/// <summary>A synced indexer without its API key.</summary>
public record SyncedIndexerResponse(
    int Id,
    string Name,
    string Url,
    int[] Categories,
    string Protocol,
    bool Enabled);

/// <summary>A download client without its password.</summary>
public record DownloadClientResponse(
    int Id,
    string Name,
    DownloadClientType Type,
    string BaseUrl,
    string? Username,
    string? Category,
    bool Enabled,
    int Priority);
