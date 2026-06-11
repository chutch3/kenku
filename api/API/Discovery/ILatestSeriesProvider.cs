namespace API.Discovery;

/// <summary>A connector that can surface its newest content as a discovery rail (e.g. GetComics' front page).</summary>
public interface ILatestSeriesProvider
{
    Task<List<DiscoveryEntry>> GetLatestSeriesAsync(CancellationToken ct);
}
