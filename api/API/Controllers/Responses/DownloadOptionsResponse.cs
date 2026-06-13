using API.Acquirers.Interfaces;

namespace API.Controllers.Responses;

/// <summary>
/// The pickable downloads for a chapter whose post bundles several, resolved live from the post.
/// Empty options with a <see cref="Reason"/> means the post genuinely needs manual handling.
/// </summary>
public record DownloadOptionsResponse(IReadOnlyList<DownloadOption> Options, string? Reason);
