using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace API.Tests.Unit.Schema;

/// <summary>
/// Guards the chokepoint itself: SeriesContext must be configured with QuerySplittingBehavior.SplitQuery.
/// The behavioural proof (queries actually split) lives in SeriesIncludeSplitQueryTests; this catches the
/// config being dropped — e.g. a new context registration that forgets to call SeriesContextOptions.Configure.
/// </summary>
public class SeriesContextOptionsTests
{
    [Fact]
    public void Configure_SetsSplitQueryBehaviour()
    {
        var builder = new DbContextOptionsBuilder<SeriesContext>();
        builder.UseNpgsql("Host=localhost;Database=x", SeriesContextOptions.Configure);

        var relational = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single();
        Assert.Equal(QuerySplittingBehavior.SplitQuery, relational.QuerySplittingBehavior);
    }
}
