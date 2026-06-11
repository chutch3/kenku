using System.Collections.Concurrent;
using API.JobRuntime.Interfaces;
using log4net;

namespace API.Discovery;

/// <summary>
/// Per-rail result cache: discovery sources are third parties we should hit once an hour, not once
/// per page view. A failing refresh serves the stale rail rather than an empty one.
/// </summary>
public class DiscoveryCache(IClock clock)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DiscoveryCache));
    private readonly ConcurrentDictionary<string, (DateTime FetchedAt, List<DiscoveryEntry> Entries)> _rails = new();

    public async Task<List<DiscoveryEntry>> GetOrRefreshAsync(string rail, TimeSpan ttl,
        Func<Task<List<DiscoveryEntry>>> fetch)
    {
        if (_rails.TryGetValue(rail, out var cached) && clock.UtcNow - cached.FetchedAt < ttl)
            return cached.Entries;
        try
        {
            List<DiscoveryEntry> fresh = await fetch();
            _rails[rail] = (clock.UtcNow, fresh);
            return fresh;
        }
        catch (Exception e)
        {
            Log.WarnFormat("Discovery rail '{0}' failed to refresh: {1}", rail, e.Message);
            return _rails.TryGetValue(rail, out var stale) ? stale.Entries : [];
        }
    }
}
