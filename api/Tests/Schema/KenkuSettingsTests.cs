using API;
using Newtonsoft.Json;
using Xunit;

namespace API.Tests.Schema;

public class KenkuSettingsTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), $"KenkuSettingsTest_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, true);
    }

    private KenkuSettings NewSettings() => new() { AppData = _tmpDir };

    [Fact]
    public void NewSettings_ShouldInitializeWithSmartDefaults()
    {
        var settings = new KenkuSettings();

        // If running locally on Linux, it should be /usr/share or ./debug
        Assert.NotNull(settings.AppData);
        Assert.Equal(40, settings.ImageCompression);
        Assert.Equal("en", settings.DownloadLanguage);
    }

    [Fact]
    public void Cors_DefaultsToAllowAnyOrigin()
    {
        var settings = new KenkuSettings();

        Assert.Empty(settings.CorsAllowedOrigins);
        Assert.True(settings.CorsAllowAnyOrigin);
    }

    [Fact]
    public void Cors_WhenOriginsConfigured_DoesNotAllowAnyOrigin()
    {
        var settings = new KenkuSettings { CorsAllowedOrigins = ["https://my-frontend.test"] };

        Assert.False(settings.CorsAllowAnyOrigin);
        Assert.Contains("https://my-frontend.test", settings.CorsAllowedOrigins);
    }

    [Fact]
    public void TorrentPath_DefaultsToDisabled()
    {
        var settings = new KenkuSettings();

        Assert.False(settings.IndexerConfigured);
        Assert.False(settings.AnyDownloadClientConfigured);
    }

    [Fact]
    public void TorrentPath_BecomesEnabled_WhenSyncedIndexerAndClientConfigured()
    {
        var settings = new KenkuSettings
        {
            SyncedIndexers = [new SyncedIndexerConfig(1, "Nyaa", "http://p/1/api", "k", [7030], "torrent", true)],
            DownloadClients = [new DownloadClientConfig(1, "qbit", DownloadClientType.QBittorrent, "http://qbittorrent:8080", "admin", "p", "comics", true, 1)]
        };

        Assert.True(settings.IndexerConfigured);
        Assert.True(settings.AnyDownloadClientConfigured);
    }

    [Fact]
    public void Indexer_BecomesConfigured_WithManualIndexersAlone_NoProwlarr()
    {
        var settings = new KenkuSettings
        {
            ManualIndexers = [new API.Indexers.ManualIndexerConfig("Tracker", "http://t.test/api", "k", [8000])]
        };

        Assert.True(settings.IndexerConfigured); // manual indexers are sufficient — not coupled to Prowlarr
    }

    [Fact]
    public void TorrentStagingDirectory_LivesUnderWorkingDirectory()
    {
        var settings = new KenkuSettings { AppData = "/tmp/x" };

        Assert.Equal("/tmp/x/kenku-api/torrent-staging", settings.TorrentStagingDirectory);
    }

    [Fact]
    public void WorkingDirectory_ShouldReflectCustomAppData()
    {
        var settings = new KenkuSettings { AppData = "/tmp/custom_manga" };

        // This confirms that changing AppData correctly flows down to the sub-paths
        Assert.Equal("/tmp/custom_manga/kenku-api", settings.WorkingDirectory);
        Assert.Equal("/tmp/custom_manga/kenku-api/settings.json", settings.SettingsFilePath);
    }

    [Fact]
    public void Serialization_ShouldRespectCustomPaths()
    {
        var original = new KenkuSettings { AppData = "/mnt/nas/kenku" };

        var json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<KenkuSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("/mnt/nas/kenku", deserialized.AppData);
    }

    [Fact]
    public void Setters_ShouldTriggerSaveWhenUsingMethods()
    {
        // Note: If you still have the "Set..." methods in the class,
        // you can verify they work as expected.
        // Since we are using DI, we usually prefer setting properties directly
        // and calling .Save() once, but testing the methods is good too.

        var settings = new KenkuSettings { AppData = "./test_save" };
        Directory.CreateDirectory(settings.WorkingDirectory);

        settings.SetDownloadLanguage("jp");

        Assert.Equal("jp", settings.DownloadLanguage);
        Assert.True(File.Exists(settings.SettingsFilePath));

        // Cleanup
        Directory.Delete("./test_save", true);
    }

    [Fact]
    public void DefaultPath_ShouldBeLinuxStandard_WhenOnLinux()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            var settings = new KenkuSettings();
            // This ensures your "Safety Net" is exactly what you expect for Swarm
            bool isExpectedPath = settings.AppData == "/usr/share" || settings.AppData == "./debug";
            Assert.True(isExpectedPath);
        }
    }

    // ---- Prowlarr-sync API key + synced-indexer / download-client list behaviour ----

    [Fact]
    public void ApiKey_IsNonEmptyByDefault()
    {
        Assert.False(string.IsNullOrWhiteSpace(NewSettings().ApiKey));
    }

    [Fact]
    public void RegenerateApiKey_ChangesKeyAndPersistsToDisk()
    {
        var settings = NewSettings();
        Directory.CreateDirectory(settings.WorkingDirectory);
        string original = settings.ApiKey;

        settings.RegenerateApiKey();

        Assert.False(string.IsNullOrWhiteSpace(settings.ApiKey));
        Assert.NotEqual(original, settings.ApiKey);
        Assert.True(File.Exists(settings.SettingsFilePath));

        var reloaded = JsonConvert.DeserializeObject<KenkuSettings>(File.ReadAllText(settings.SettingsFilePath))!;
        Assert.Equal(settings.ApiKey, reloaded.ApiKey);
    }

    [Fact]
    public void AddOrUpdateSyncedIndexer_AddsThenUpdatesByNameAndProtocol()
    {
        var settings = NewSettings();
        Directory.CreateDirectory(settings.WorkingDirectory);

        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Nyaa", "http://p/1/api", "k", [7030], "torrent", true));
        Assert.Single(settings.SyncedIndexers);
        Assert.True(settings.SyncedIndexers[0].Id > 0);

        // same name + protocol => update (and keep the assigned id), not add
        int existingId = settings.SyncedIndexers[0].Id;
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Nyaa", "http://p/2/api", "k2", [7030, 7000], "torrent", false));
        Assert.Single(settings.SyncedIndexers);
        Assert.Equal("http://p/2/api", settings.SyncedIndexers[0].Url);
        Assert.Equal("k2", settings.SyncedIndexers[0].ApiKey);
        Assert.False(settings.SyncedIndexers[0].Enabled);
        Assert.Equal(existingId, settings.SyncedIndexers[0].Id);

        // different protocol => add
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Nyaa", "http://p/3/api", "k3", [7030], "usenet", true));
        Assert.Equal(2, settings.SyncedIndexers.Count);
    }

    [Fact]
    public void RemoveSyncedIndexer_RemovesByNameAndProtocol()
    {
        var settings = NewSettings();
        Directory.CreateDirectory(settings.WorkingDirectory);
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Nyaa", "http://p/1/api", "k", [7030], "torrent", true));
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Nyaa", "http://p/3/api", "k3", [7030], "usenet", true));

        settings.RemoveSyncedIndexer("Nyaa", "torrent");

        Assert.Single(settings.SyncedIndexers);
        Assert.Equal("usenet", settings.SyncedIndexers[0].Protocol);
    }

    [Fact]
    public void SyncedIndexers_RoundTripThroughSerialization()
    {
        var settings = NewSettings();
        settings.SyncedIndexers.Add(new SyncedIndexerConfig(1, "Nyaa", "http://p/1/api", "k", [7030, 7000], "torrent", true));

        var copy = JsonConvert.DeserializeObject<KenkuSettings>(JsonConvert.SerializeObject(settings))!;

        Assert.Single(copy.SyncedIndexers);
        Assert.Equal("Nyaa", copy.SyncedIndexers[0].Name);
        Assert.Equal([7030, 7000], copy.SyncedIndexers[0].Categories);
        Assert.Equal("torrent", copy.SyncedIndexers[0].Protocol);
    }

    [Fact]
    public void DownloadClientCrud_AddUpdateRemove_AssignsIdsAndPersists()
    {
        var settings = NewSettings();
        Directory.CreateDirectory(settings.WorkingDirectory);
        Assert.False(settings.AnyDownloadClientConfigured);

        int id = settings.AddDownloadClient(new DownloadClientConfig(0, "qbit", DownloadClientType.QBittorrent, "http://qbit", "u", "p", "comics", true, 1));
        Assert.True(id > 0);
        Assert.Single(settings.DownloadClients);
        Assert.True(settings.AnyDownloadClientConfigured);

        Assert.True(settings.UpdateDownloadClient(new DownloadClientConfig(id, "qbit2", DownloadClientType.QBittorrent, "http://qbit2", "u", "p", "comics", true, 2)));
        Assert.Equal("qbit2", settings.DownloadClients[0].Name);
        Assert.Equal("http://qbit2", settings.DownloadClients[0].BaseUrl);

        Assert.False(settings.UpdateDownloadClient(new DownloadClientConfig(9999, "x", DownloadClientType.QBittorrent, "http://x", null, null, null, true, 1)));

        Assert.True(settings.RemoveDownloadClient(id));
        Assert.Empty(settings.DownloadClients);
        Assert.False(settings.AnyDownloadClientConfigured);
        Assert.False(settings.RemoveDownloadClient(id));
    }

    [Fact]
    public void AnyDownloadClientConfigured_FalseWhenOnlyDisabledClients()
    {
        var settings = NewSettings();
        Directory.CreateDirectory(settings.WorkingDirectory);
        settings.AddDownloadClient(new DownloadClientConfig(0, "qbit", DownloadClientType.QBittorrent, "http://qbit", null, null, null, false, 1));

        Assert.False(settings.AnyDownloadClientConfigured);
    }
}
