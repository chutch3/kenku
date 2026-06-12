using API.Indexers;
using Xunit;

namespace API.Tests.Unit.Indexers;

public class ReleaseTitleParserTests
{
    [Theory]
    // title                                                  | series              | issue  | year
    [InlineData("Saga 060 (2024) (Digital) (Zone-Empire)",      "Saga",               "60",   2024)]
    [InlineData("The Walking Dead 100 (2012)",                  "The Walking Dead",   "100",  2012)]
    [InlineData("Batman (2016) 050",                            "Batman",             "50",   2016)]
    [InlineData("Invincible Compendium 001 (2011)",             "Invincible Compendium", "1", 2011)]
    [InlineData("Saga #60",                                     "Saga",               "60",   null)]
    [InlineData("Monstress Vol. 1 (2016)",                      "Monstress",          "1",    2016)]
    [InlineData("Saga 60.5 (2024)",                             "Saga",               "60.5", 2024)]
    [InlineData("Some Ongoing Comic",                           "Some Ongoing Comic", null,   null)]
    public void Parse_ExtractsSeriesIssueYear(string title, string expectedSeries, string? expectedIssue, int? expectedYear)
    {
        var parsed = ReleaseTitleParser.Parse(title);

        Assert.Equal(expectedSeries, parsed.SeriesTitle);
        Assert.Equal(expectedIssue, parsed.IssueNumber);
        Assert.Equal(expectedYear, parsed.Year);
    }

    [Theory]
    // title                                                       | series        | start | end
    [InlineData("Invincible 001-144 (2003-2018) (digital)",          "Invincible",   1,      144)]
    [InlineData("The Walking Dead #1-193 Complete (Digital)",        "The Walking Dead", 1,  193)]
    [InlineData("Saga 001 - 066 (2012-2023)",                        "Saga",         1,      66)]
    public void Parse_ExtractsIssueRangesFromPackReleases(string title, string expectedSeries, int start, int end)
    {
        var parsed = ReleaseTitleParser.Parse(title);

        Assert.Equal(expectedSeries, parsed.SeriesTitle);
        Assert.Equal((start, end), parsed.IssueRange);
        Assert.Null(parsed.IssueNumber);
    }

    [Theory]
    // Volume packs are NOT issue ranges: "Vol. 1-10" is ~60 issues, so fanning it out as issues
    // 1..10 would mislabel the library. They stay unparsed until volume packs are modelled.
    [InlineData("Monstress Vol. 1-9 (2016-2024)")]
    [InlineData("Saga v1-10 (TPB)")]
    public void Parse_DoesNotTreatVolumePacksAsIssueRanges(string title)
    {
        Assert.Null(ReleaseTitleParser.Parse(title).IssueRange);
    }

    [Fact]
    public void Parse_SingleIssueRelease_HasNoRange()
    {
        Assert.Null(ReleaseTitleParser.Parse("Saga 060 (2024)").IssueRange);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptySeries()
    {
        var parsed = ReleaseTitleParser.Parse("");
        Assert.Equal("", parsed.SeriesTitle);
        Assert.Null(parsed.IssueNumber);
    }

    [Fact]
    public void Parse_IssueIsValidChapterNumber_AcceptedByChapterCtor()
    {
        // The parsed issue string must satisfy Chapter's chapter-number contract.
        var parsed = ReleaseTitleParser.Parse("Saga 060 (2024)");
        Assert.Equal("60", parsed.IssueNumber);
    }
}
