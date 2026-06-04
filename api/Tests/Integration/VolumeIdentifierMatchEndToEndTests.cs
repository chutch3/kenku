using API.Schema.SeriesContext;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Outside-in: the real app resolves volumes through the HTTP endpoint. A series with a preset AniList link
/// must be linked to the MangaDex entry whose <c>links.al</c> matches — even over a higher-scoring decoy,
/// and even when the matched entry has no volume aggregate yet.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeIdentifierMatchEndToEndTests : OutboundHttpIntegrationTest
{
    private async Task<string> SeedLinkedSeries(string aniListUrl)
    {
        string libraryKey = await SeedLibrary();
        return await App.WithSeriesContext(async ctx =>
        {
            var library = await ctx.FileLibraries.FindAsync(libraryKey);
            var manga = new Series("Fire Punch", "d", "u", SeriesReleaseStatus.Continuing, [], [],
                [new Link("AniList", aniListUrl)], [], library);
            manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
            ctx.Series.Add(manga);
            ctx.Chapters.Add(new Chapter(manga, "1", null, null) { Downloaded = true, FileName = "Ch.1.cbz" });
            await ctx.SaveChangesAsync();
            return manga.Key;
        });
    }

    private async Task ResolveAndExpectLink(string mangaKey, string expectedExternalId)
    {
        (await App.CreateClient().PostAsync("/v2/Maintenance/ResolveMissingVolumes", null)).EnsureSuccessStatusCode();

        bool linked = await WaitUntil(async () => (await MetadataSourceFor(mangaKey)).ExternalId == expectedExternalId);
        Assert.True(linked, $"series was not linked to {expectedExternalId}");
    }

    [Fact]
    public async Task Resolve_LinksSeriesToTheMangaDexEntryMatchingItsAniListId_OverAHigherScoringDecoy()
    {
        StubMangaDexSearch(IntegrationFixtures.DecoyVsTrueSearch);
        StubMangaDexAggregate(IntegrationFixtures.AggregateChapter1Volume1);
        StubWikipediaEmpty();

        string mangaKey = await SeedLinkedSeries("https://anilist.co/manga/87170");

        await ResolveAndExpectLink(mangaKey, "true-uuid");
    }

    [Fact]
    public async Task Resolve_PersistsAnIdMatch_EvenWhenTheMatchedEntryHasNoVolumeAggregate()
    {
        // An AniList id match is authoritative — persist the link even though MangaDex has no volume tags yet.
        StubMangaDexSearch(IntegrationFixtures.SingleAniListMatchSearch);
        StubMangaDexAggregate(IntegrationFixtures.EmptyAggregate);
        StubWikipediaEmpty();

        string mangaKey = await SeedLinkedSeries("https://anilist.co/manga/87170");

        await ResolveAndExpectLink(mangaKey, "fp-uuid");
    }

    [Fact]
    public async Task Resolve_PersistsAnIdMatch_EvenWhenTheVolumeFetchThrows()
    {
        // The matched entry's aggregate returns malformed JSON → the volume fetch throws. The authoritative
        // id match must still be persisted (volumes can come later), not rolled back on the fetch failure.
        StubMangaDexSearch(IntegrationFixtures.SingleAniListMatchSearch);
        StubMangaDexAggregate("this-is-not-json");
        StubWikipediaEmpty();

        string mangaKey = await SeedLinkedSeries("https://anilist.co/manga/87170");

        await ResolveAndExpectLink(mangaKey, "fp-uuid");
    }
}
