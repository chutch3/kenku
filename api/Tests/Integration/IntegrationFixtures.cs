namespace API.Tests.Integration;

/// <summary>
/// Shared HTTP-edge fixtures for the outside-in integration tests, so the MangaDex/connector response
/// shapes live in one place instead of being copy-pasted (and drifting) across test files.
/// </summary>
internal static class IntegrationFixtures
{
    /// <summary>A WeebCentral series page that links out to AniList entry 87170.</summary>
    public const string WeebCentralFirePunchHtml =
        """<html><head><title>Fire Punch | Weeb Central</title></head><body><a href="https://anilist.co/manga/87170/Fire-Punch">AniList</a></body></html>""";

    /// <summary>The AniList entry URL the above page yields.</summary>
    public const string FirePunchAniListUrl = "https://anilist.co/manga/87170/Fire-Punch";

    /// <summary>MangaDex search: a decoy that wins on title+count, and the true entry that only matches by
    /// AniList id 87170. Identifier matching must pick <c>true-uuid</c>.</summary>
    public const string DecoyVsTrueSearch =
        """{"result":"ok","response":"collection","data":[{"id":"decoy-uuid","type":"manga","attributes":{"title":{"en":"Fire Punch"},"lastChapter":"1","links":{"al":"999"}},"relationships":[]},{"id":"true-uuid","type":"manga","attributes":{"title":{"en":"Fire Punch (Remaster)"},"lastChapter":"83","links":{"al":"87170"}},"relationships":[]}]}""";

    /// <summary>MangaDex search with a single entry whose AniList id is 87170 (id <c>fp-uuid</c>).</summary>
    public const string SingleAniListMatchSearch =
        """{"result":"ok","response":"collection","data":[{"id":"fp-uuid","type":"manga","attributes":{"title":{"en":"Something Else"},"lastChapter":"83","links":{"al":"87170"}},"relationships":[]}]}""";

    /// <summary>An aggregate that places chapter 1 in volume 1.</summary>
    public const string AggregateChapter1Volume1 =
        """{"result":"ok","volumes":{"1":{"volume":"1","chapters":{"1":{"chapter":"1"}}}}}""";

    /// <summary>An aggregate with no volumes at all.</summary>
    public const string EmptyAggregate = """{"result":"ok","volumes":{}}""";

    /// <summary>A MediaWiki parse response with no chapter-list wikitext.</summary>
    public const string EmptyWikipedia = """{"parse":{"wikitext":{"*":""}}}""";
}
