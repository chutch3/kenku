using API.Discovery;
using Xunit;

namespace API.Tests.Unit.Discovery;

/// <summary>Parsing is pinned against a captured live response (2026-06-11).</summary>
public class AniListClientTests
{
    private const string TrendingJson = """
    {"data":{"Page":{"media":[
      {"title":{"romaji":"Pick Me Up!","english":"Pick Me Up"},
       "coverImage":{"large":"https://s4.anilist.co/file/bx159441.jpg"},
       "siteUrl":"https://anilist.co/manga/159441",
       "description":"1-Star heroes are just fodder.<br><br>(Source: Tapas)"},
      {"title":{"romaji":"ONE PIECE","english":null},
       "coverImage":{"large":"https://s4.anilist.co/file/bx30013.jpg"},
       "siteUrl":"https://anilist.co/manga/30013",
       "description":null}
    ]}}}
    """;

    [Fact]
    public void ParseTrending_MapsEntries_PreferringEnglishTitles()
    {
        List<DiscoveryEntry> entries = AniListClient.ParseTrending(TrendingJson);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Pick Me Up", entries[0].Title);
        Assert.Equal("https://s4.anilist.co/file/bx159441.jpg", entries[0].CoverUrl);
        Assert.Equal("https://anilist.co/manga/159441", entries[0].Url);
        Assert.Equal("AniList", entries[0].Source);
        // Roman title when no english one; html stripped from the blurb.
        Assert.Equal("ONE PIECE", entries[1].Title);
        Assert.DoesNotContain("<br>", entries[0].Blurb);
        Assert.Contains("1-Star heroes", entries[0].Blurb);
    }

    [Fact]
    public void ParseTrending_Throws_OnAnUnexpectedShape()
    {
        Assert.ThrowsAny<Exception>(() => AniListClient.ParseTrending("""{"errors":[{"message":"boom"}]}"""));
    }
}
