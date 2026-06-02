using API.Schema.SeriesContext;

namespace API.Services;

/// <summary>Where a volume stands with respect to VolumeCBZ bundling.</summary>
public enum VolumeBundleState
{
    /// <summary>Series isn't VolumeCBZ, so bundling doesn't apply.</summary>
    NotApplicable,
    /// <summary>Already bundled into a single .cbz.</summary>
    Bundled,
    /// <summary>Closed and fully downloaded — eligible to bundle now.</summary>
    ReadyToBundle,
    /// <summary>Fully downloaded but still the trailing volume; left alone until it is superseded.</summary>
    PendingNewerVolume,
    /// <summary>Not all of the volume's chapters are downloaded yet.</summary>
    Incomplete
}

/// <summary>Per-volume bundling status, surfaced so a "mixed" library is explainable.</summary>
public record VolumeBundleStatus(int VolumeNumber, int TotalChapters, int DownloadedChapters, VolumeBundleState State, string Reason);

/// <summary>
/// Decides which of a series' volumes are ready to be bundled into a single .cbz under
/// <see cref="LibraryLayout.VolumeCBZ"/>, and explains the state of every volume. Only "closed"
/// volumes are bundled — all chapters downloaded and either a later volume exists or the series will
/// gain no more chapters — so the in-progress trailing volume is left as individual chapter files
/// until it is superseded, avoiding repeated unbundle/rebundle churn.
/// </summary>
public static class VolumeBundlePolicy
{
    public static IReadOnlyList<int> VolumesReadyToBundle(Series series)
        => Classify(series)
            .Where(v => v.State == VolumeBundleState.ReadyToBundle)
            .Select(v => v.VolumeNumber)
            .ToList();

    /// <summary>Classify every numbered volume of <paramref name="series"/>. Volume-less chapters are excluded.</summary>
    public static IReadOnlyList<VolumeBundleStatus> Classify(Series series)
    {
        List<Chapter> numbered = series.Chapters.Where(c => c.VolumeNumber is not null).ToList();
        if (numbered.Count == 0)
            return [];

        int maxVolume = numbered.Max(c => c.VolumeNumber!.Value);
        // A terminal series will gain no further chapters, so even its last volume is closed.
        bool seriesTerminal = series.ReleaseStatus is SeriesReleaseStatus.Completed or SeriesReleaseStatus.Cancelled;
        bool isVolumeCBZ = series.LibraryLayout == LibraryLayout.VolumeCBZ;

        var report = new List<VolumeBundleStatus>();
        foreach (IGrouping<int, Chapter> volume in numbered.GroupBy(c => c.VolumeNumber!.Value).OrderBy(g => g.Key))
        {
            int total = volume.Count();
            int downloaded = volume.Count(c => c.Downloaded);

            (VolumeBundleState state, string reason) = Evaluate();
            report.Add(new VolumeBundleStatus(volume.Key, total, downloaded, state, reason));

            (VolumeBundleState, string) Evaluate()
            {
                if (!isVolumeCBZ)
                    return (VolumeBundleState.NotApplicable, $"bundling not enabled (layout is {series.LibraryLayout})");
                if (volume.Any(c => c.IsBundled))
                    return (VolumeBundleState.Bundled, "bundled into a single .cbz");
                if (downloaded < total)
                    return (VolumeBundleState.Incomplete, $"{downloaded}/{total} chapters downloaded");
                bool closed = seriesTerminal || volume.Key < maxVolume;
                return closed
                    ? (VolumeBundleState.ReadyToBundle, "all chapters downloaded; ready to bundle")
                    : (VolumeBundleState.PendingNewerVolume, "complete, but the latest volume — waiting for a newer volume before bundling");
            }
        }

        return report;
    }
}
