using API.Acquirers.Interfaces;
using API.DownloadClients.Interfaces;
using API.Indexers.Interfaces;
using API;
using API.Acquirers;
using API.Extensions;
using API.Indexers;
using API.MangaConnectors;
using API.HttpRequesters;
using API.DownloadClients;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Workers;

/// <summary>
/// Exercises the DI wiring of the indexer-search and torrent-acquisition paths.
/// Search is always registered (indexers are pushed in live by Prowlarr); acquisition is gated on a
/// configured download client.
/// </summary>
public class TorrentFlowIntegrationTests
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(TorrentFlowIntegrationTests));

    private static KenkuSettings NewSettings() =>
        new() { AppData = Path.Combine(Path.GetTempPath(), $"kenku-test-{Guid.NewGuid():N}") };

    private static ServiceProvider Build(KenkuSettings settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton<RateLimitHandler>();
        services.AddTorrentAcquisitionPath(settings, Log);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void IndexersAndDownloadClient_RegistersSearchAndAcquisition()
    {
        var settings = NewSettings();
        settings.SyncedIndexers.Add(new SyncedIndexerConfig(1, "Nyaa", "http://p/1/api", "k", [7030], "torrent", true));
        settings.DownloadClients.Add(new DownloadClientConfig(1, "qbit", DownloadClientType.QBittorrent, "http://qbit", null, null, null, true, 1));

        ServiceProvider provider = Build(settings);

        Assert.NotNull(provider.GetService<IIndexerClient>());
        Assert.NotNull(provider.GetService<IDownloadClientFactory>());
        Assert.NotNull(provider.GetService<IDownloadClient>());
        Assert.NotNull(provider.GetService<IChapterAcquirer>());
    }

    [Fact]
    public void IndexersButNoDownloadClient_SearchPresent_AcquisitionAbsent()
    {
        var settings = NewSettings();
        settings.SyncedIndexers.Add(new SyncedIndexerConfig(1, "Nyaa", "http://p/1/api", "k", [7030], "torrent", true));

        ServiceProvider provider = Build(settings);

        // Search path is always available (indexers can be pushed live by Prowlarr).
        Assert.NotNull(provider.GetService<IIndexerClient>());
        // Acquisition path is gated on an enabled download client.
        Assert.Null(provider.GetService<IDownloadClient>());
        Assert.Null(provider.GetService<IChapterAcquirer>());
    }

    [Fact]
    public void NothingConfigured_SearchStillRegistered()
    {
        var settings = NewSettings();

        ServiceProvider provider = Build(settings);

        Assert.NotNull(provider.GetService<IIndexerClient>());
        Assert.Null(provider.GetService<IDownloadClient>());
    }

    [Fact]
    public void DisabledDownloadClientOnly_AcquisitionAbsent()
    {
        var settings = NewSettings();
        settings.DownloadClients.Add(new DownloadClientConfig(1, "qbit", DownloadClientType.QBittorrent, "http://qbit", null, null, null, false, 1));

        ServiceProvider provider = Build(settings);

        Assert.Null(provider.GetService<IDownloadClient>());
    }
}
