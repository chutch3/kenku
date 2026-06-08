using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace API.Schema.SeriesContext;

/// <summary>
/// Shared Npgsql configuration for <see cref="SeriesContext"/>, called by both Program.cs and the test
/// host so production and tests build the context identically — one chokepoint, no drift.
/// </summary>
public static class SeriesContextOptions
{
    /// <summary>
    /// SeriesContext eager-loads several collections at once (chapters, authors, tags, links, alt-titles).
    /// SplitQuery issues one query per collection; as a single JOIN the rows fan out to the product of
    /// every collection and time out the command on large series.
    /// </summary>
    public static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
}
