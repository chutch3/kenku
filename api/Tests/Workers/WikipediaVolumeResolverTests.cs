using System.Net;
using API.Schema.SeriesContext;
using API.Workers.MaintenanceWorkers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace API.Tests.Workers;

public class WikipediaVolumeResolverTests
{
    private static readonly FileLibrary Library = new("/tmp", "Test Library");

    // Two volumes in the real {{Graphic novel list}} / {{Numbered list}} shape. Includes a {{Nihongo}}
    // template and a [[piped|link]] to prove their internal pipes are NOT counted as list items.
    private const string Fixture = """
        {{Graphic novel list/header
        | Language = Japanese
        }}
        {{Graphic novel list
        | VolumeNumber = 1
        | ChapterList =
        {{Numbered list|start = 1
        | {{Nihongo|"A"|あ|A}}
        | {{Nihongo|"B"|い|B}}
        | {{Nihongo|"C [[Some link|display]]"|う|C}}
        }}
        | ChapterListCol2 =
        {{Numbered list|start = 4
        | {{Nihongo|"D"|え|D}}
        | {{Nihongo|"E"|お|E}}
        }}
        }}
        {{Graphic novel list
        | VolumeNumber = 2
        | ChapterList =
        {{Numbered list|start = 6
        | item1
        | item2
        | item3
        | item4
        | item5
        }}
        | ChapterListCol2 =
        {{Numbered list|start = 11
        | item6
        | item7
        | item8
        | item9
        }}
        }}
        """;

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static Series Manga() =>
        new("Dandadan", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], Library);

    [Fact]
    public void ParseVolumeMap_MapsEveryChapterToItsVolume()
    {
        var map = WikipediaVolumeResolver.ParseVolumeMap(Fixture);

        Assert.Equal(14, map.Count);
        // Volume 1 = chapters 1–5 (3 in ChapterList + 2 in ChapterListCol2)
        foreach (var ch in new[] { "1", "2", "3", "4", "5" })
            Assert.Equal(1, map[ch]);
        // Volume 2 = chapters 6–14
        foreach (var ch in new[] { "6", "10", "14" })
            Assert.Equal(2, map[ch]);
    }

    [Fact]
    public void ParseVolumeMap_IgnoresPipesInsideNestedTemplatesAndLinks()
    {
        // If nested pipes were miscounted, volume 1 would not be exactly 5 chapters.
        var map = WikipediaVolumeResolver.ParseVolumeMap(Fixture);
        Assert.Equal(5, map.Values.Count(v => v == 1));
        Assert.False(map.ContainsKey("6") && map["6"] == 1);
    }

    [Fact]
    public async Task ResolveAsync_FetchesChapterListPageAndReturnsMap()
    {
        var requestedUrls = new List<string>();
        var payload = new JObject { ["parse"] = new JObject { ["wikitext"] = new JObject { ["*"] = Fixture } } };
        var handler = new FakeHttpMessageHandler(req =>
        {
            requestedUrls.Add(req.RequestUri!.ToString());
            return Json(payload.ToString());
        });

        var resolver = new WikipediaVolumeResolver(new HttpClient(handler));
        var map = await resolver.ResolveAsync(Manga(), [], CancellationToken.None);

        Assert.Contains(requestedUrls, u => u.Contains("Dandadan") && u.Contains("action=parse"));
        Assert.Equal(1, map["1"]);
        Assert.Equal(2, map["14"]);
    }

    [Fact]
    public async Task ResolveAsync_WhenPageMissing_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            Json("""{ "error": { "code": "missingtitle", "info": "The page you specified doesn't exist." } }"""));

        var resolver = new WikipediaVolumeResolver(new HttpClient(handler));
        var map = await resolver.ResolveAsync(Manga(), [], CancellationToken.None);

        Assert.Empty(map);
    }

    [Fact]
    public async Task ResolveAsync_WhenRequestFails_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var resolver = new WikipediaVolumeResolver(new HttpClient(handler));
        var map = await resolver.ResolveAsync(Manga(), [], CancellationToken.None);

        Assert.Empty(map);
    }

    [Fact]
    public void Metadata_IsExactConfidenceNamedWikipedia()
    {
        var resolver = new WikipediaVolumeResolver(new HttpClient());
        Assert.Equal("Wikipedia", resolver.SourceName);
        Assert.Equal(MetadataConfidence.Exact, resolver.Confidence);
    }
}
