using API.Migrations.Manga;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Proves the one-time dedup migration removes a rescoped chapter-source predecessor (e.g. ComicHubFree
/// "issue-1" superseded by "the-boys/issue-1") while leaving genuinely distinct re-uploads of the same
/// chapter number (two unrelated WeebCentral ULIDs) intact — the generic id-shape rule, not a per-series
/// or per-connector hack. Relational SQL (right()/||), so it needs the Postgres fixture.
/// </summary>
public class ReconcileScopedConnectorChapterIdsTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private string _dbName = null!;
    private string _cs = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        _cs = _postgres.GetConnectionString(_dbName);
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();

        var manga = new Series("Test", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], []);
        ctx.Series.Add(manga);

        // Rescoped pair: the unscoped predecessor and its "{slug}/issue-N" successor.
        var rescoped = new Chapter(manga, "1", null);
        var stale = new SourceId<Chapter>(rescoped, "ComicHubFree", "issue-1", "http://c/1", true);
        var current = new SourceId<Chapter>(rescoped, "ComicHubFree", "the-boys/issue-1", "http://c/1", true);
        rescoped.SourceIds.Add(stale);
        rescoped.SourceIds.Add(current);

        // Distinct re-uploads: two unrelated ULIDs for the same number — neither is a prefix of the other.
        var reuploaded = new Chapter(manga, "161", null);
        var uploadA = new SourceId<Chapter>(reuploaded, "WeebCentral", "01JBVRQK3NMN0RBP5HBQ48HYVC", "http://w/a", true);
        var uploadB = new SourceId<Chapter>(reuploaded, "WeebCentral", "01K12EMZ6VEBTPQCPMAZCWF1S2", "http://w/b", false);
        reuploaded.SourceIds.Add(uploadA);
        reuploaded.SourceIds.Add(uploadB);

        ctx.Chapters.AddRange(rescoped, reuploaded);
        ctx.ChapterSourceIds.AddRange(stale, current, uploadA, uploadB);
        await ctx.SaveChangesAsync();
    }

    public Task DisposeAsync() => _postgres.DropDatabaseAsync(_dbName);

    private SeriesContext NewContext() =>
        new(new DbContextOptionsBuilder<SeriesContext>().UseNpgsql(_cs).Options);

    [Fact]
    public async Task Dedup_RemovesRescopedPredecessor_AndKeepsDistinctReuploads()
    {
        await using (var ctx = NewContext())
            // DedupSql is pinned to the historical table name (the migration runs before the rename);
            // adapt it to the current schema to re-verify the dedup invariant still holds.
            await ctx.Database.ExecuteSqlRawAsync(
                ReconcileScopedConnectorChapterIds.DedupSql.Replace("MangaConnectorToChapter", "ChapterSourceIds"));

        await using var verify = NewContext();
        var ids = await verify.ChapterSourceIds.Select(s => s.IdOnConnectorSite).ToListAsync();

        Assert.DoesNotContain("issue-1", ids);                          // stale predecessor removed
        Assert.Contains("the-boys/issue-1", ids);                       // rescoped survivor kept
        Assert.Contains("01JBVRQK3NMN0RBP5HBQ48HYVC", ids);             // distinct re-uploads untouched
        Assert.Contains("01K12EMZ6VEBTPQCPMAZCWF1S2", ids);
        Assert.Equal(3, ids.Count);
    }
}
