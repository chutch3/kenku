using API;
using API.Connectors;
using API.Extensions;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Unit.Extensions;

/// <summary>
/// The torrent feature is gated by <see cref="KenkuSettings.TorrentEnabled"/>: when off, the torrent
/// search source and acquisition path aren't registered at all, so torrents simply don't exist.
/// </summary>
public class TorrentAcquisitionPathTests
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(TorrentAcquisitionPathTests));

    private static KenkuSettings Settings(bool torrentEnabled) => new()
    {
        AppData = Path.Combine(Path.GetTempPath(), "kenku-tap-" + Guid.NewGuid().ToString("N")),
        TorrentEnabled = torrentEnabled,
    };

    [Fact]
    public void WhenTorrentDisabled_RegistersNoTorrentSeriesSource()
    {
        var services = new ServiceCollection();
        services.AddTorrentAcquisitionPath(Settings(false), Log);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(SeriesSource));
    }

    [Fact]
    public void WhenTorrentEnabled_RegistersTheTorrentSeriesSource()
    {
        var services = new ServiceCollection();
        services.AddTorrentAcquisitionPath(Settings(true), Log);
        Assert.Contains(services, d => d.ServiceType == typeof(SeriesSource));
    }
}
