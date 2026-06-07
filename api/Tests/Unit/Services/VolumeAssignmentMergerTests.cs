using API.Services.Interfaces;
using API.Schema.SeriesContext;
using API.Services;
using Xunit;

namespace API.Tests.Unit.Services;

public class VolumeAssignmentMergerTests
{
    private static readonly FileLibrary Library = new("/tmp", "Test Library");

    private static Series Manga() =>
        new("Test", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], Library);

    private static Chapter Chapter(string number, int? volume = null, MetadataConfidence? confidence = null)
    {
        var ch = new Chapter(Manga(), number, volume, $"Title {number}");
        ch.MetadataConfidence = confidence;
        return ch;
    }

    private static VolumeResolverResult Result(MetadataConfidence confidence, params (string ch, int vol)[] entries) =>
        new(confidence, entries.ToDictionary(e => e.ch, e => e.vol));

    [Fact]
    public void Fills_UnassignedChapterFromSource()
    {
        var ch = Chapter("5");
        var changes = VolumeAssignmentMerger.ComputeChanges([ch], [Result(MetadataConfidence.Exact, ("5", 1))]);

        var change = Assert.Single(changes);
        Assert.Equal(1, change.Volume);
        Assert.Equal(MetadataConfidence.Exact, change.Confidence);
    }

    [Fact]
    public void NeverOverridesManualAssignment()
    {
        var ch = Chapter("5", volume: 99, confidence: MetadataConfidence.Manual);
        var changes = VolumeAssignmentMerger.ComputeChanges([ch], [Result(MetadataConfidence.Exact, ("5", 1))]);

        Assert.Empty(changes);
    }

    [Fact]
    public void OverwritesStaleHeuristicWithExact()
    {
        // The Dandadan vol-17 case: a wrong heuristic guess must yield to an exact source on re-run.
        var ch = Chapter("139", volume: 17, confidence: MetadataConfidence.Heuristic);
        var changes = VolumeAssignmentMerger.ComputeChanges([ch], [Result(MetadataConfidence.Exact, ("139", 17))]);

        var change = Assert.Single(changes);
        Assert.Equal(MetadataConfidence.Exact, change.Confidence);
    }

    [Fact]
    public void NeverNullsOutAChapterNoSourceResolves()
    {
        // A transient source outage must not wipe an existing assignment.
        var ch = Chapter("5", volume: 3, confidence: MetadataConfidence.Heuristic);
        var changes = VolumeAssignmentMerger.ComputeChanges([ch], [Result(MetadataConfidence.Exact, ("999", 1))]);

        Assert.Empty(changes);
    }

    [Fact]
    public void DoesNotDowngradeExactToHeuristic()
    {
        var ch = Chapter("5", volume: 5, confidence: MetadataConfidence.Exact);
        var changes = VolumeAssignmentMerger.ComputeChanges([ch], [Result(MetadataConfidence.Heuristic, ("5", 6))]);

        Assert.Empty(changes);
    }

    [Fact]
    public void WithinSameConfidence_EarlierSourceWins()
    {
        var ch = Chapter("5");
        var changes = VolumeAssignmentMerger.ComputeChanges([ch],
        [
            Result(MetadataConfidence.Exact, ("5", 2)),  // higher priority
            Result(MetadataConfidence.Exact, ("5", 3)),
        ]);

        Assert.Equal(2, Assert.Single(changes).Volume);
    }

    [Fact]
    public void HigherConfidenceWins_RegardlessOfOrder()
    {
        var ch = Chapter("5");
        var changes = VolumeAssignmentMerger.ComputeChanges([ch],
        [
            Result(MetadataConfidence.Heuristic, ("5", 9)),  // listed first but weaker
            Result(MetadataConfidence.Exact, ("5", 2)),
        ]);

        var change = Assert.Single(changes);
        Assert.Equal(2, change.Volume);
        Assert.Equal(MetadataConfidence.Exact, change.Confidence);
    }

    [Fact]
    public void NoOp_WhenAlreadyMatches()
    {
        var ch = Chapter("5", volume: 2, confidence: MetadataConfidence.Exact);
        var changes = VolumeAssignmentMerger.ComputeChanges([ch], [Result(MetadataConfidence.Exact, ("5", 2))]);

        Assert.Empty(changes);
    }

    [Fact]
    public void OverwritesWithinSameTier_WhenValueChanged()
    {
        // MangaDex re-tagged a chapter into a different volume — same tier, new value applies.
        var ch = Chapter("5", volume: 2, confidence: MetadataConfidence.Exact);
        var changes = VolumeAssignmentMerger.ComputeChanges([ch], [Result(MetadataConfidence.Exact, ("5", 3))]);

        Assert.Equal(3, Assert.Single(changes).Volume);
    }
}
