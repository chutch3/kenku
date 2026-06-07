using API;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;

namespace API.Tests.Unit.Schema;

public class MangaContextUpsertTests
{
    private SeriesContext CreateContext() =>
        new(new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task UpsertManga_NewManga_InsertsIntoDatabase()
    {
        using var ctx = CreateContext();
        var manga = MangaTests.MakeTestManga();
        var mcId = new SourceId<Series>(manga, "MangaDex", "ext-id-1", "https://mangadex.org", true);

        var result = await ctx.UpsertManga(manga, mcId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(manga.Key, result!.Value.manga.Key);
        Assert.Equal(1, await ctx.Series.CountAsync());
    }

    [Fact]
    public async Task UpsertManga_NewManga_AttachesMangaConnectorId()
    {
        using var ctx = CreateContext();
        var manga = MangaTests.MakeTestManga();
        var mcId = new SourceId<Series>(manga, "MangaDex", "ext-id-1", "https://mangadex.org", true);

        var result = await ctx.UpsertManga(manga, mcId, CancellationToken.None);

        Assert.NotNull(result);
        var saved = await ctx.Series
            .Include(m => m.SourceIds)
            .FirstAsync(m => m.Key == manga.Key);
        Assert.Single(saved.SourceIds);
        Assert.Equal("MangaDex", saved.SourceIds.First().MangaConnectorName);
    }

    [Fact]
    public async Task UpsertManga_NewManga_PersistsExternalLinks()
    {
        // The whole point of capturing connector links is that they survive to the DB so a later step
        // can match the series to a metadata source by identifier.
        using var ctx = CreateContext();
        var manga = new Series("Fire Punch", "d", "u", SeriesReleaseStatus.Completed, [], [],
            [new Link("AniList", "https://anilist.co/manga/87170")], []);
        var mcId = new SourceId<Series>(manga, "WeebCentral", "wc-1", "https://weebcentral.com/series/wc-1", true);

        await ctx.UpsertManga(manga, mcId, CancellationToken.None);

        var saved = await ctx.Series.Include(m => m.Links).FirstAsync(m => m.Key == manga.Key);
        Assert.Contains(saved.Links, l => l.LinkProvider == "AniList" && l.LinkUrl == "https://anilist.co/manga/87170");
    }

    [Fact]
    public async Task UpsertManga_ExistingManga_BackfillsNewExternalLinks()
    {
        // A series imported before link capture existed has no links. Re-importing it (now that the
        // connector surfaces them) must backfill the links onto the existing row, not drop them.
        using var ctx = CreateContext();
        var first = new Series("Fire Punch", "d", "u", SeriesReleaseStatus.Completed, [], [], [], []);
        var firstId = new SourceId<Series>(first, "WeebCentral", "wc-1", "https://weebcentral.com/series/wc-1", true);
        await ctx.UpsertManga(first, firstId, CancellationToken.None);

        var second = new Series("Fire Punch", "d", "u", SeriesReleaseStatus.Completed, [], [],
            [new Link("AniList", "https://anilist.co/manga/87170")], []);
        var secondId = new SourceId<Series>(second, "WeebCentral", "wc-1", "https://weebcentral.com/series/wc-1", true);
        await ctx.UpsertManga(second, secondId, CancellationToken.None);

        var saved = await ctx.Series.Include(m => m.Links).FirstAsync(m => m.Name == "Fire Punch");
        Assert.Contains(saved.Links, l => l.LinkUrl == "https://anilist.co/manga/87170");
    }

    [Fact]
    public async Task UpsertManga_ExistingManga_DoesNotInsertDuplicate()
    {
        using var ctx = CreateContext();
        var manga = MangaTests.MakeTestManga();
        var mcId = new SourceId<Series>(manga, "MangaDex", "ext-id-1", "https://mangadex.org", true);
        await ctx.UpsertManga(manga, mcId, CancellationToken.None);

        // Upsert again with same connector name + id
        var manga2 = MangaTests.MakeTestManga();
        manga2.Name = manga.Name; // same title so FindMangaLike matches
        var mcId2 = new SourceId<Series>(manga2, "MangaDex", "ext-id-1", "https://mangadex.org", true);
        await ctx.UpsertManga(manga2, mcId2, CancellationToken.None);

        Assert.Equal(1, await ctx.Series.CountAsync());
    }

    [Fact]
    public async Task UpsertManga_ExistingManga_NewConnector_AddsConnectorId()
    {
        using var ctx = CreateContext();
        var manga = MangaTests.MakeTestManga();
        var mcId1 = new SourceId<Series>(manga, "MangaDex", "ext-id-1", "https://mangadex.org", true);
        await ctx.UpsertManga(manga, mcId1, CancellationToken.None);

        // Same manga, different connector
        var manga2 = MangaTests.MakeTestManga();
        manga2.Name = manga.Name;
        var mcId2 = new SourceId<Series>(manga2, "Mangaworld", "mw-id-1", "https://mangaworld.ac", false);
        await ctx.UpsertManga(manga2, mcId2, CancellationToken.None);

        var saved = await ctx.Series
            .Include(m => m.SourceIds)
            .FirstAsync(m => m.Key == manga.Key);
        Assert.Equal(2, saved.SourceIds.Count);
    }

    [Fact]
    public async Task UpsertManga_ExistingConnectorId_UpdatesWebsiteUrl()
    {
        using var ctx = CreateContext();
        var manga = MangaTests.MakeTestManga();
        var mcId = new SourceId<Series>(manga, "MangaDex", "ext-id-1", "https://old.url", true);
        await ctx.UpsertManga(manga, mcId, CancellationToken.None);

        var manga2 = MangaTests.MakeTestManga();
        manga2.Name = manga.Name;
        var mcId2 = new SourceId<Series>(manga2, "MangaDex", "ext-id-1", "https://new.url", true);
        var result = await ctx.UpsertManga(manga2, mcId2, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("https://new.url", result!.Value.id.WebsiteUrl);
    }

    [Fact]
    public async Task AddMangaToContext_ConvenienceOverload_BehavesLikeUpsertManga()
    {
        using var ctx = CreateContext();
        var manga = MangaTests.MakeTestManga();
        var mcId = new SourceId<Series>(manga, "MangaDex", "ext-id-1", "https://mangadex.org", true);

        var result = await ctx.AddMangaToContext((manga, mcId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, await ctx.Series.CountAsync());
    }
}
