using API.Acquirers;
using API.Indexers;
using API.MangaConnectors;
using API.HttpRequesters;
using API.DownloadClients;
using log4net;
using Microsoft.Extensions.DependencyInjection;

namespace API.Extensions;

/// <summary>
/// DI registration helpers. Keep Program.cs lean by grouping coherent registration blocks here.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the indexer search path (always) and the torrent acquisition path (when a download
    /// client is configured).
    ///
    /// The search path is ALWAYS registered: indexers are pushed in live by Prowlarr (the Mylar app
    /// contract), so the set can grow at runtime with no restart. <see cref="SyncedIndexerProvider"/>
    /// re-reads <see cref="KenkuSettings.SyncedIndexers"/> on every search, and
    /// <see cref="AggregateIndexerSearch"/> re-queries its providers per search. With zero indexers,
    /// search simply returns empty — harmless.
    ///
    /// The acquisition path (download client, release selector, acquirer, completion worker) is gated
    /// on <see cref="KenkuSettings.AnyDownloadClientConfigured"/>.
    /// </summary>
    public static IServiceCollection AddTorrentAcquisitionPath(this IServiceCollection services, KenkuSettings settings, ILog log)
    {
        // ---- Search path (always on) ----

        if (settings.ManualIndexers.Count > 0)
            services.AddSingleton<IIndexerProvider>(sp =>
                new ConfiguredIndexerProvider(
                    new HttpClient(sp.GetRequiredService<RateLimitHandler>(), disposeHandler: false),
                    settings.ManualIndexers));

        // Prowlarr-synced indexers. The provider reads settings live, so we always register it.
        services.AddSingleton<IIndexerProvider>(sp =>
            new SyncedIndexerProvider(
                new HttpClient(sp.GetRequiredService<RateLimitHandler>(), disposeHandler: false),
                settings));

        services.AddSingleton<IIndexerClient>(sp =>
            new AggregateIndexerSearch(sp.GetServices<IIndexerProvider>()));

        // The user-facing torrent-backed series source. Registering it as a SeriesSource makes it
        // appear in search + chapter discovery; its Kind=Torrent routes chapters through the acquirer.
        services.AddSingleton<SeriesSource>(sp =>
            new IndexerBackedSeriesSource(sp.GetRequiredService<IIndexerClient>(), settings));

        // ---- Acquisition path (gated on a configured download client) ----

        if (!settings.AnyDownloadClientConfigured)
        {
            log.Info("Torrent acquisition path disabled (no enabled download client configured). Search path is active.");
            return services;
        }

        log.Info("A download client is configured — registering torrent acquisition path.");

        services.AddSingleton<IDownloadClientFactory>(sp =>
            new DownloadClientFactory(() =>
                new HttpClient(sp.GetRequiredService<RateLimitHandler>(), disposeHandler: false)));

        services.AddSingleton<IDownloadClient>(sp =>
            sp.GetRequiredService<IDownloadClientFactory>().SelectActive(settings)
            ?? throw new InvalidOperationException("No enabled download client is configured."));

        services.AddSingleton(_ => new ReleaseSelector
        {
            MinSeeders = settings.ReleaseMinSeeders,
            PreferredTokens = settings.ReleasePreferredTokens,
            BlockedTokens = settings.ReleaseBlockedTokens
        });

        services.AddSingleton<IChapterAcquirer>(sp =>
            new TorrentAcquirer(
                sp.GetRequiredService<IIndexerClient>(),
                sp.GetRequiredService<IDownloadClient>(),
                sp.GetRequiredService<ReleaseSelector>(),
                new TorrentAcquirerSettings(settings.TorrentStagingDirectory, settings.IndexerComicCategories)));

        services.AddHostedService<API.JobRuntime.TorrentCompletionReconciler>();

        return services;
    }
}
