using API.Schema.SeriesContext;
using Xunit;

namespace API.Tests.Unit.Schema;

/// <summary>
/// Cover writes go through <see cref="Series.SetCover"/> so they're deterministic instead of
/// last-writer-wins: an empty cover is filled by anyone, but a present cover is only replaced by an
/// equal-or-higher-ranked source (Provider &lt; Connector &lt; User).
/// </summary>
public class SeriesCoverPrecedenceTests
{
    private static Series NewSeries(string cover) =>
        new("S", "d", cover, SeriesReleaseStatus.Continuing, [], [], [], []);

    [Fact]
    public void FillsAnEmptyCoverFromAnySource()
    {
        var s = NewSeries("");
        Assert.True(s.SetCover("https://mal/cover.jpg", CoverSource.Provider));
        Assert.Equal("https://mal/cover.jpg", s.CoverUrl);
        Assert.Equal(CoverSource.Provider, s.CoverSource);
    }

    [Fact]
    public void ProviderDoesNotOverwriteAConnectorCover()
    {
        var s = NewSeries("https://connector/cover.jpg"); // constructor ranks an initial cover as Connector
        Assert.False(s.SetCover("https://mal/cover.jpg", CoverSource.Provider));
        Assert.Equal("https://connector/cover.jpg", s.CoverUrl);
    }

    [Fact]
    public void ConnectorOverwritesAProviderCover()
    {
        var s = NewSeries("");
        s.SetCover("https://mal/cover.jpg", CoverSource.Provider);
        Assert.True(s.SetCover("https://connector/cover.jpg", CoverSource.Connector));
        Assert.Equal("https://connector/cover.jpg", s.CoverUrl);
        Assert.Equal(CoverSource.Connector, s.CoverSource);
    }

    [Fact]
    public void EqualRankReplacesAChangedUrl_SoConnectorCdnRotationStillTracks()
    {
        var s = NewSeries("https://connector/old.jpg");
        Assert.True(s.SetCover("https://connector/new.jpg", CoverSource.Connector));
        Assert.Equal("https://connector/new.jpg", s.CoverUrl);
    }

    [Fact]
    public void UserChoiceOutranksAConnectorCover()
    {
        var s = NewSeries("https://connector/cover.jpg");
        Assert.True(s.SetCover("https://feed/cover.jpg", CoverSource.User));
        Assert.Equal("https://feed/cover.jpg", s.CoverUrl);
        // And a later routine connector sync must not clobber the user's pick.
        Assert.False(s.SetCover("https://connector/other.jpg", CoverSource.Connector));
        Assert.Equal("https://feed/cover.jpg", s.CoverUrl);
    }

    [Fact]
    public void EmptyOrUnchangedUrlIsANoOp()
    {
        var s = NewSeries("https://connector/cover.jpg");
        Assert.False(s.SetCover("", CoverSource.User));
        Assert.False(s.SetCover("   ", CoverSource.User));
        Assert.False(s.SetCover("https://connector/cover.jpg", CoverSource.Connector));
    }
}
