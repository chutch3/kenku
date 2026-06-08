using System.Data.Common;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Regression guard for the cartesian-explosion that timed out large series in prod (v0.9.0):
/// MangaIncludeAll() eager-loads six collections (chapters, sourceIds, authors, tags, alt-titles,
/// links). As ONE JOINed query the result fans out to the product of every collection's rows, so a
/// series with hundreds of chapters blew past the 60s command timeout. AsSplitQuery() must keep it as
/// one query per collection — proven here by asserting no single command returns the cartesian product.
/// Relational-only (the bug cannot exist on InMemory), so this needs the Postgres fixture.
/// </summary>
public class SeriesIncludeSplitQueryTests : IAsyncLifetime
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

        var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-split-" + _dbName), "Lib");
        ctx.FileLibraries.Add(library);
        // Two rows in every collection: a single JOINed query fans out to 2*2*2*2*2 = 32 rows.
        var series = new Series("Test", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing,
            [new Author("A1"), new Author("A2")],
            [new SeriesTag("T1"), new SeriesTag("T2")],
            [new Link("P1", "http://l1"), new Link("P2", "http://l2")],
            [new AltTitle("en", "Alt1"), new AltTitle("jp", "Alt2")],
            library);
        ctx.Series.Add(series);
        ctx.Chapters.Add(new Chapter(series, "1", null, null));
        ctx.Chapters.Add(new Chapter(series, "2", null, null));
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DropDatabaseAsync(_dbName);

    [Fact]
    public async Task MangaIncludeAll_SplitsCollections_AndLoadsThemCorrectly()
    {
        var counter = new SelectCommandCounter();
        var opts = new DbContextOptionsBuilder<SeriesContext>()
            .UseNpgsql(_cs)
            .AddInterceptors(counter)
            .Options;
        await using var ctx = new SeriesContext(opts);

        var series = await ctx.MangaIncludeAll().FirstAsync();

        // Correctness: every collection is fully and exactly loaded (EF dedups, so this passes even
        // with the cartesian bug — which is why it alone never caught the regression).
        Assert.Equal(2, series.Chapters.Count);
        Assert.Equal(2, series.Authors.Count);
        Assert.Equal(2, series.MangaTags.Count);
        Assert.Equal(2, series.AltTitles.Count);
        Assert.Equal(2, series.Links.Count);

        // The actual guard: one cartesian query would be a single SELECT; AsSplitQuery issues a
        // separate SELECT per collection. More than one SELECT proves the JOIN fan-out is gone.
        Assert.True(counter.SelectCommands > 1,
            $"MangaIncludeAll ran as a single cartesian query ({counter.SelectCommands} SELECT). Use AsSplitQuery().");
    }

    private sealed class SelectCommandCounter : DbCommandInterceptor
    {
        private int _selects;
        public int SelectCommands => _selects;

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref _selects);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
