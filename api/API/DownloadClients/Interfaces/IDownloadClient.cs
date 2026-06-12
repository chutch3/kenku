namespace API.DownloadClients.Interfaces;

/// <summary>
/// Owned abstraction over an external download client (qBittorrent, Deluge, etc.). Kenku uses
/// a caller-supplied <c>tag</c> as the download's identifier — this sidesteps the cost of extracting
/// the BitTorrent info-hash from a magnet/.torrent on the Kenku side. Implementations tag the
/// download so it can be looked up again by the same tag.
/// </summary>
public interface IDownloadClient
{
    /// <summary>
    /// Adds <paramref name="downloadUrl"/> (a magnet or .torrent URL) to the client. Returns the
    /// <paramref name="tag"/> on success, null on failure.
    /// </summary>
    Task<string?> Add(string downloadUrl, string saveDir, string tag, CancellationToken ct);

    /// <summary>Returns the current status of the torrent tagged with <paramref name="tag"/>, or null if not found.</summary>
    Task<DownloadStatus?> GetStatus(string tag, CancellationToken ct);

    /// <summary>Removes the torrent tagged with <paramref name="tag"/>. No-ops if not found.</summary>
    Task Remove(string tag, bool deleteData, CancellationToken ct);

    /// <summary>
    /// Every download in Kenku's category, with its tag and status. Lets callers find downloads whose
    /// tags aren't chapter-keyed (pack torrents) and surface in-flight progress.
    /// </summary>
    Task<IReadOnlyList<DownloadEntry>> List(CancellationToken ct);
}

/// <summary>One download as the client reports it.</summary>
public record DownloadEntry(string Tag, string Name, DownloadStatus Status, double Progress, int Seeders);

public abstract record DownloadStatus
{
    public sealed record Downloading(double Progress) : DownloadStatus;
    public sealed record Completed(string SavePath) : DownloadStatus;
    public sealed record Errored(string Reason) : DownloadStatus;
}
