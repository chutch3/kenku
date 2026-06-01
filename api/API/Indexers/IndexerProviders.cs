namespace API.Indexers;

/// <summary>A manually-configured Torznab/Newznab indexer (name + endpoint + key + categories).</summary>
public record ManualIndexerConfig(string Name, string Url, string ApiKey, int[] Categories);

/// <summary>
/// Yields the indexers a user added by hand (Torznab/Newznab endpoints stored in settings). No
/// Prowlarr involvement — this is the "you can add them" half of the *arr model.
/// </summary>
public class ConfiguredIndexerProvider(HttpClient http, IReadOnlyList<ManualIndexerConfig> indexers) : IIndexerProvider
{
    public Task<IReadOnlyList<IIndexer>> GetIndexersAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<IIndexer>>(
            indexers.Select(i => (IIndexer)new TorznabIndexer(http, i.Name, i.Url, i.ApiKey, i.Categories)).ToArray());
}

/// <summary>
/// Exposes the indexers Prowlarr pushed/synced into Kenku via the Mylar application contract.
/// Reads <see cref="KenkuSettings.SyncedIndexers"/> live on every call (snapshotting under a lock)
/// so Prowlarr's pushed updates take effect with no Kenku restart. Each enabled config becomes a
/// <see cref="TorznabIndexer"/> pointing at Prowlarr's per-indexer Torznab endpoint.
/// </summary>
public class SyncedIndexerProvider(HttpClient http, KenkuSettings settings) : IIndexerProvider
{
    public Task<IReadOnlyList<IIndexer>> GetIndexersAsync(CancellationToken ct)
    {
        var indexers = new List<IIndexer>();
        foreach (SyncedIndexerConfig config in settings.SnapshotSyncedIndexers())
        {
            if (!config.Enabled)
                continue;
            indexers.Add(new TorznabIndexer(http, config.Name, config.Url, config.ApiKey, config.Categories));
        }
        return Task.FromResult<IReadOnlyList<IIndexer>>(indexers);
    }
}
