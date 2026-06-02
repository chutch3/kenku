using API.Schema.SeriesContext;

namespace API.Services;

/// <summary>
/// Decides which of a series' volumes are ready to be bundled into a single .cbz under
/// <see cref="LibraryLayout.VolumeCBZ"/>. Only "closed" volumes are bundled — see
/// <see cref="VolumesReadyToBundle"/> — so the in-progress trailing volume is left as individual
/// chapter files until it is superseded, avoiding repeated unbundle/rebundle churn.
/// </summary>
public static class VolumeBundlePolicy
{
    public static IReadOnlyList<int> VolumesReadyToBundle(Series series)
    {
        if (series.LibraryLayout != LibraryLayout.VolumeCBZ)
            return [];

        List<Chapter> numbered = series.Chapters.Where(c => c.VolumeNumber is not null).ToList();
        if (numbered.Count == 0)
            return [];

        int maxVolume = numbered.Max(c => c.VolumeNumber!.Value);

        // A terminal series will gain no further chapters, so even its last volume is closed.
        bool seriesTerminal = series.ReleaseStatus is SeriesReleaseStatus.Completed or SeriesReleaseStatus.Cancelled;

        var ready = new List<int>();
        foreach (IGrouping<int, Chapter> volume in numbered.GroupBy(c => c.VolumeNumber!.Value).OrderBy(g => g.Key))
        {
            bool isClosed = seriesTerminal || volume.Key < maxVolume;
            if (!isClosed)
                continue; // trailing/in-progress volume may still grow
            if (volume.Any(c => c.IsBundled))
                continue; // already bundled
            if (volume.All(c => c.Downloaded))
                ready.Add(volume.Key);
        }

        return ready;
    }
}
