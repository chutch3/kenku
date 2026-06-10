using System.Collections.Concurrent;
using API.JobRuntime.Interfaces;

namespace API.Indexers;

/// <summary>
/// Per-indexer rate-limit memory shared across the transient <see cref="TorznabIndexer"/> instances.
/// When an indexer returns HTTP 429 we record a cooldown (honouring Retry-After) and skip searching
/// it until the window elapses, instead of hammering it every chapter and burning the daily quota.
/// </summary>
public class IndexerCooldown(IClock clock)
{
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, DateTime> _until = new();

    /// <summary>The active cooldown's end time for an indexer, or null if it is free to search.</summary>
    public DateTime? CooldownUntil(string indexer) =>
        _until.TryGetValue(indexer, out DateTime until) && until > clock.UtcNow ? until : null;

    public bool IsCoolingDown(string indexer) => CooldownUntil(indexer) is not null;

    /// <summary>Record a 429 — cool the indexer down for Retry-After if given, else a default window.</summary>
    public void RecordRateLimited(string indexer, TimeSpan? retryAfter) =>
        _until[indexer] = clock.UtcNow + (retryAfter ?? DefaultCooldown);
}
