namespace API.DownloadClients;

public interface IDownloadClientFactory
{
    IReleaseDownloadClient Create(DownloadClientConfig config);

    /// <summary>Returns a client for the lowest-Priority enabled download client, or null if none.</summary>
    IReleaseDownloadClient? SelectActive(KenkuSettings settings);
}

/// <summary>
/// Builds an <see cref="IReleaseDownloadClient"/> from a <see cref="DownloadClientConfig"/>. Additional
/// client types (Transmission/Deluge) plug in here against the same interface.
/// </summary>
public class DownloadClientFactory(Func<HttpClient> httpClientFactory) : IDownloadClientFactory
{
    public IReleaseDownloadClient Create(DownloadClientConfig config) =>
        config.Type switch
        {
            DownloadClientType.QBittorrent => new QBittorrentClient(
                httpClientFactory(), config.BaseUrl, config.Username ?? "", config.Password ?? ""),
            _ => throw new NotSupportedException($"Unsupported download client type: {config.Type}")
        };

    public IReleaseDownloadClient? SelectActive(KenkuSettings settings)
    {
        DownloadClientConfig? config = settings.DownloadClients
            .Where(c => c.Enabled)
            .OrderBy(c => c.Priority)
            .FirstOrDefault();
        return config is null ? null : Create(config);
    }
}
