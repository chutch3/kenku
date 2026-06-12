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
    public void ParseMedia_MapsEntries_PreferringEnglishTitles()
    {
        List<DiscoveryEntry> entries = AniListClient.ParseMedia(TrendingJson);

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
    public void ParseMedia_Throws_OnAnUnexpectedShape()
    {
        Assert.ThrowsAny<Exception>(() => AniListClient.ParseMedia("""{"errors":[{"message":"boom"}]}"""));
    }

    [Fact]
    public void BuildRequestBody_OmitsAbsentFilters()
    {
        string body = AniListClient.BuildRequestBody(AniListShelf.Trending, 20);

        Assert.Contains("TRENDING_DESC", body);
        Assert.Contains("\"perPage\":20", body);
        // GraphQL treats an unprovided nullable variable as "argument not supplied" — an explicit
        // null would instead filter on genre being null.
        Assert.DoesNotContain("genre", body.Split("variables")[1]);
        Assert.DoesNotContain("startDateGreater", body.Split("variables")[1]);
    }

    [Fact]
    public void BuildRequestBody_IncludesGenreAndFuzzyStartDate()
    {
        string body = AniListClient.BuildRequestBody(new AniListShelf("POPULARITY_DESC", Genre: "Action", MinStartYear: 2026), 10);

        Assert.Contains("\"genre\":\"Action\"", body);
        Assert.Contains("\"startDateGreater\":20260000", body);
    }
}
