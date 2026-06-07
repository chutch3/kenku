using API.DownloadClients.Interfaces;
namespace API.DownloadClients;

public interface IDownloadClientFactory
{
    IDownloadClient Create(DownloadClientConfig config);

    /// <summary>Returns a client for the lowest-Priority enabled download client, or null if none.</summary>
    IDownloadClient? SelectActive(KenkuSettings settings);
}

/// <summary>
/// Builds an <see cref="IDownloadClient"/> from a <see cref="DownloadClientConfig"/>. Additional
/// client types (Transmission/Deluge) plug in here against the same interface.
/// </summary>
public class DownloadClientFactory(Func<HttpClient> httpClientFactory) : IDownloadClientFactory
{
    public IDownloadClient Create(DownloadClientConfig config) =>
        config.Type switch
        {
            DownloadClientType.QBittorrent => new QBittorrentClient(
                httpClientFactory(), config.BaseUrl, config.Username ?? "", config.Password ?? ""),
            _ => throw new NotSupportedException($"Unsupported download client type: {config.Type}")
        };

    public IDownloadClient? SelectActive(KenkuSettings settings)
    {
        DownloadClientConfig? config = settings.SnapshotDownloadClients()
            .Where(c => c.Enabled)
            .OrderBy(c => c.Priority)
            .FirstOrDefault();
        return config is null ? null : Create(config);
    }
}
