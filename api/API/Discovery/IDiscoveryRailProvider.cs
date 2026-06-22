using API.Connectors;

namespace API.Discovery;

/// <summary>
/// A source that contributes one or more Discover rails: it declares them and serves each rail's entries.
/// Generalizes the old single-rail latest-provider so a source can expose several rails (e.g. GetComics
/// Fresh + Weekly, MangaDex Popular + Latest). The set of available rails is the live aggregation of all
/// providers' <see cref="Rails"/> — there is no separate static registry.
/// </summary>
public interface IDiscoveryRailProvider
{
    /// <summary>The rails this provider offers (stable id, display label, content axis).</summary>
    IReadOnlyList<DiscoveryRail> Rails { get; }

    /// <summary>Entries for one declared rail id. Unknown ids return empty.</summary>
    Task<List<DiscoveryEntry>> GetRailAsync(string railId, CancellationToken ct);
}

/// <summary>A rail a provider declares: a stable id, a display label, which content axis it belongs to,
/// and a global <paramref name="Order"/> so the aggregator can interleave rails from different providers
/// into a deterministic, fixed page order.</summary>
public record DiscoveryRail(string Id, string Label, ContentType ContentType, int Order);
