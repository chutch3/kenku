using API.Schema.SeriesContext;

namespace API.Acquirers.Interfaces;

/// <summary>
/// Implemented by DirectArchive connectors whose chapters carry a viewer/post URL instead of the
/// archive itself. <see cref="DirectArchiveAcquirer"/> resolves through this seam at download time,
/// so the connector owns its site's link structure and the acquirer stays a dumb downloader.
/// </summary>
public interface IArchiveUrlResolver
{
    /// <summary>Resolves the chapter's WebsiteUrl to a fetchable archive URL, or explains why a
    /// human has to do it (the reason is user-facing and surfaces on the failed job).</summary>
    Task<ArchiveResolution> ResolveArchiveUrl(SourceId<Chapter> chapter, CancellationToken ct);
}

/// <summary>Outcome of resolving a post/viewer URL to a downloadable archive.</summary>
public abstract record ArchiveResolution
{
    public sealed record Resolved(string Url) : ArchiveResolution;
    public sealed record Manual(string Reason) : ArchiveResolution;
}
