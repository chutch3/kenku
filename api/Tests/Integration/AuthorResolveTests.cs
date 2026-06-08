using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Regression guard for the prod PK_Authors violation (v0.9.2): authors share a name-derived key across
/// series, so a metadata refresh that handed EF fresh Author instances tried to INSERT one that already
/// existed (shared with another series) → "23505: duplicate key value violates unique constraint
/// PK_Authors". ResolveAuthorsAsync must reuse the existing row. Postgres-only: InMemory does not enforce
/// the unique constraint the same way, so the bug only reproduces on a real relational engine.
/// </summary>
public class AuthorResolveTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private string _dbName = null!;
    private string _cs = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        _cs = _postgres.GetConnectionString(_dbName);

        var opts = new DbContextOptionsBuilder<SeriesContext>().UseNpgsql(_cs).Options;
        await using var ctx = new SeriesContext(opts);
        await ctx.Database.MigrateAsync();

        // Series A already owns author "Shared" — commits the Authors row other series will collide with.
        var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-author-" + _dbName), "Lib");
        ctx.FileLibraries.Add(library);
        var seriesA = new Series("A", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing,
            [new Author("Shared")], [], [], [], library);
        ctx.Series.Add(seriesA);
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DropDatabaseAsync(_dbName);

    [Fact]
    public async Task ResolveAuthors_ReusesExistingRow_NoDuplicateKeyViolation()
    {
        var opts = new DbContextOptionsBuilder<SeriesContext>().UseNpgsql(_cs).Options;
        await using var ctx = new SeriesContext(opts);

        var seriesB = new Series("B", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing,
            [], [], [], [], await ctx.FileLibraries.FirstAsync());
        ctx.Series.Add(seriesB);

        // "Shared" already exists (series A); "Solo" is new. The pre-fix code (new Author(name)) would
        // throw 23505 on SaveChanges here.
        seriesB.Authors = await ctx.ResolveAuthorsAsync(["Shared", "Solo", "Shared"], CancellationToken.None);
        await ctx.SaveChangesAsync();

        // Exactly two author rows total — "Shared" reused (not duplicated), "Solo" inserted, the
        // duplicate "Shared" in the input de-duped.
        Assert.Equal(2, await ctx.Authors.CountAsync());
        Assert.Equal(2, seriesB.Authors.Count);
    }
}
