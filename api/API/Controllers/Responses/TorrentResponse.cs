using API.DownloadClients.Interfaces;

namespace API.Controllers.Responses;

/// <summary>One download as the torrent client reports it, for the activity page.</summary>
public record TorrentResponse(string Name, string Tag, string State, double Progress, int Seeders, string? Error)
{
    public static TorrentResponse From(DownloadEntry e) => e.Status switch
    {
        DownloadStatus.Completed => new(e.Name, e.Tag, "completed", e.Progress, e.Seeders, null),
        DownloadStatus.Errored err => new(e.Name, e.Tag, "errored", e.Progress, e.Seeders, err.Reason),
        _ => new(e.Name, e.Tag, "downloading", e.Progress, e.Seeders, null),
    };
}
