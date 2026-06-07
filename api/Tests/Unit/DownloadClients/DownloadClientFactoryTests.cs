using API.DownloadClients.Interfaces;
using API;
using API.DownloadClients;
using Xunit;

namespace API.Tests.Unit.DownloadClients;

public class DownloadClientFactoryTests
{
    [Fact]
    public void Create_QBittorrent_ReturnsQBittorrentClient()
    {
        var factory = new DownloadClientFactory(() => new HttpClient());
        var config = new DownloadClientConfig(1, "qbit", DownloadClientType.QBittorrent, "http://qbit", "u", "p", "comics", true, 1);

        IDownloadClient client = factory.Create(config);

        Assert.IsType<QBittorrentClient>(client);
    }

    [Fact]
    public void SelectActive_ReturnsLowestPriorityEnabledClient()
    {
        var settings = NewSettings();
        settings.AddDownloadClient(new DownloadClientConfig(0, "low", DownloadClientType.QBittorrent, "http://low", null, null, null, true, 5));
        settings.AddDownloadClient(new DownloadClientConfig(0, "high", DownloadClientType.QBittorrent, "http://high", null, null, null, true, 1));
        settings.AddDownloadClient(new DownloadClientConfig(0, "disabled", DownloadClientType.QBittorrent, "http://disabled", null, null, null, false, 0));

        var factory = new DownloadClientFactory(() => new HttpClient());

        IDownloadClient? client = factory.SelectActive(settings);

        Assert.NotNull(client);
        Assert.IsType<QBittorrentClient>(client);
    }

    [Fact]
    public void SelectActive_NoEnabledClients_ReturnsNull()
    {
        var settings = NewSettings();
        settings.AddDownloadClient(new DownloadClientConfig(0, "off", DownloadClientType.QBittorrent, "http://off", null, null, null, false, 0));

        var factory = new DownloadClientFactory(() => new HttpClient());

        Assert.Null(factory.SelectActive(settings));
    }

    private static KenkuSettings NewSettings() =>
        new() { AppData = Path.Combine(Path.GetTempPath(), $"kenku-test-{Guid.NewGuid():N}") };
}
