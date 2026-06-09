using API.Connectors;
using API.Schema.SeriesContext;

namespace API.Acquirers.Interfaces;

/// <summary>
/// Produces the .cbz archive for a chapter at a specified path on disk. Implementations encapsulate
/// "how to turn a Chapter into a file" — image-by-image scrape, direct archive download, torrent
/// hand-off, etc. — behind a single seam the chapter download worker can call uniformly.
/// </summary>
public interface IChapterAcquirer
{
    /// <summary>The acquisition kind this implementation handles. Used by the dispatcher to pick the
    /// right acquirer for a given connector's declared Kind.</summary>
    AcquisitionKind Kind { get; }

    /// <summary>
    /// Acquires the chapter, writing a .cbz to <paramref name="saveArchiveFilePath"/> when this
    /// implementation produces the file itself. Implementations are responsible for logging their
    /// own errors; a <see cref="AcquireResult.Failed"/> reason is user-facing.
    /// </summary>
    /// <param name="chapter">The chapter source-id row, with Obj+ParentManga eagerly loaded.</param>
    /// <param name="source">The connector that originally produced this chapter.</param>
    /// <param name="saveArchiveFilePath">Absolute path the .cbz must be written to.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AcquireResult> AcquireAsync(
        SourceId<Chapter> chapter,
        SeriesSource source,
        string saveArchiveFilePath,
        CancellationToken ct);
}

/// <summary>
/// Outcome of an acquisition attempt. <see cref="Deferred"/> is a success, not a failure: the chapter
/// was handed off to an external client and a poll-then-finalize path (TorrentCompletionReconciler)
/// owns its completion — treating it as a failure re-adds the same download on every retry.
/// </summary>
public abstract record AcquireResult
{
    public sealed record Acquired(string Path) : AcquireResult;
    public sealed record Deferred : AcquireResult;
    public sealed record Failed(string Reason) : AcquireResult;
}
