namespace API.Discovery;

/// <summary>Owned seam for AniList's public GraphQL API (no key required).</summary>
public interface IAniListClient
{
    Task<List<DiscoveryEntry>> GetTrendingMangaAsync(int limit, CancellationToken ct);
}
