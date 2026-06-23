using API.Acquirers;
using Xunit;

namespace API.Tests.Unit.Acquirers;

public class MissingPageDetectorTests
{
    private const long Real = 1000 * 1553;   // a real comic page (~1.5M px²)
    private const long Holder = 80 * 104;     // the placeholder (~8k px²)

    [Fact]
    public void Empty_FlagsNothing() =>
        Assert.Empty(MissingPageDetector.Detect([]));

    [Fact]
    public void AllRealPages_FlagsNothing() =>
        Assert.Empty(MissingPageDetector.Detect([Real, Real, Real, Real, Real]));

    [Fact]
    public void SparseMissingPage_FlagsJustThatPage()
    {
        // one placeholder among many real pages
        var flagged = MissingPageDetector.Detect([Real, Real, Holder, Real, Real]);
        Assert.Equal([2], flagged);
    }

    [Fact]
    public void MajorityMissing_StillFlagsThePlaceholders()
    {
        // The Crossed case: placeholders are the majority (13 of 21). Anchoring on the MAX (not the
        // median/percentile) is what makes these get flagged rather than the real pages.
        long[] areas = [Real, Real, Holder, Real, Holder, Holder, Real, Real, Holder, Holder, Holder, Holder];
        var flagged = MissingPageDetector.Detect(areas);
        Assert.Equal([2, 4, 5, 8, 9, 10, 11], flagged);
    }

    [Fact]
    public void EveryPageIsPlaceholder_FlagsThemAll()
    {
        var flagged = MissingPageDetector.Detect([Holder, Holder, Holder]);
        Assert.Equal([0, 1, 2], flagged);
    }

    [Fact]
    public void DoublePageSpread_DoesNotFlagSinglePages()
    {
        // A spread is the largest page; single pages are ~50% of it and must not be flagged (15% floor).
        long spread = Real * 2;
        Assert.Empty(MissingPageDetector.Detect([Real, spread, Real, Real]));
    }

    [Fact]
    public void FailedOrUndecodablePage_AreaZero_IsFlagged()
    {
        var flagged = MissingPageDetector.Detect([Real, 0, Real]);
        Assert.Equal([1], flagged);
    }

    [Fact]
    public void SingleRealPage_FlagsNothing() =>
        Assert.Empty(MissingPageDetector.Detect([Real]));

    [Fact]
    public void SinglePlaceholderPage_IsFlagged() =>
        Assert.Equal([0], MissingPageDetector.Detect([Holder]));
}
