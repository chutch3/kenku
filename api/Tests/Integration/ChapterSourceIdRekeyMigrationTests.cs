using API.Migrations.Manga;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Proves the ChapterSourceIdKeyIncludesChapter migration recomputes the chapter source-id PK in place to
/// exactly the key the SourceId&lt;Chapter&gt; constructor now produces — so after the migration the DB and
/// the app agree on every key (existing rows resolve, nothing re-inserts), with no row dropped and
/// UseForDownload preserved. Postgres md5()/|| must match .NET TokenGen, so it needs the Postgres fixture.
/// </summary>
public class ChapterSourceIdRekeyMigrationTests : IAsyncLifetime
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
    }

    public Task DisposeAsync() => _postgres.DropDatabaseAsync(_dbName);

    private SeriesContext NewContext() =>
        new(new DbContextOptionsBuilder<SeriesContext>().UseNpgsql(_cs).Options);

    [Fact]
    public async Task Rekey_RecomputesTheKeyToMatchTheConstructor_PreservingTheRowAndItsFlags()
    {
        var series = new Series("Test", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], []);
        var chapter = new Chapter(series, "1", null);
        var sourceId = new SourceId<Chapter>(chapter, "GetComics", "1", "http://g/1", useForDownload: true);
        string expectedKey = sourceId.Key; // the constructor's new chapter-scoped key

        await using (var ctx = NewContext())
        {
            ctx.Series.Add(series);
            ctx.Chapters.Add(chapter);
            ctx.ChapterSourceIds.Add(sourceId);
            await ctx.SaveChangesAsync();

            // Simulate pre-migration prod state: the row carries the OLD (connector, id) key.
            await ctx.Database.ExecuteSqlRawAsync(ChapterSourceIdKeyIncludesChapter.RevertSql);
        }

        await using (var ctx = NewContext())
            await ctx.Database.ExecuteSqlRawAsync(ChapterSourceIdKeyIncludesChapter.RekeySql);

        await using var verify = NewContext();
        var row = Assert.Single(await verify.ChapterSourceIds.ToListAsync());
        Assert.Equal(expectedKey, row.Key); // Postgres md5 == .NET TokenGen → app and DB agree
        Assert.Equal(chapter.Key, row.ObjId); // still bound to its chapter
        Assert.True(row.UseForDownload); // download selection preserved
    }
}
