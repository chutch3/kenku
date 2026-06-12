using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace API.Schema.DiscoveryContext;

/// <summary>
/// Used only by EF design-time tools (migrations). Not used at runtime.
/// </summary>
public class DiscoveryContextDesignTimeFactory : IDesignTimeDbContextFactory<DiscoveryContext>
{
    public DiscoveryContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DiscoveryContext>();
        // Placeholder connection string — only used by EF design-time tooling
        optionsBuilder.UseNpgsql("Host=localhost;Database=kenku;Username=kenku;Password=kenku");
        return new DiscoveryContext(optionsBuilder.Options);
    }
}
