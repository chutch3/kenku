namespace API.Controllers.Requests;

/// <summary>A user-chosen download URL from <see cref="Responses.DownloadOptionsResponse"/>.</summary>
public record PinnedDownloadRequest(string Url);
