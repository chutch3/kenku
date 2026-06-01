namespace API.Controllers.Requests;

public record SetDownloadClientRecord(
    int Id,
    string Name,
    DownloadClientType Type,
    string BaseUrl,
    string? Username,
    string? Password,
    string? Category,
    bool Enabled,
    int Priority);
