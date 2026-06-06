using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace API.Schema.JobsContext;

/// <summary>
/// Used only by EF design-time tools (migrations). Not used at runtime.
/// </summary>
public class JobsContextDesignTimeFactory : IDesignTimeDbContextFactory<JobsContext>
{
    public JobsContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<JobsContext>();
        // Placeholder connection string — only used by EF design-time tooling
        optionsBuilder.UseNpgsql("Host=localhost;Database=kenku;Username=kenku;Password=kenku");
        return new JobsContext(optionsBuilder.Options);
    }
}
