using Microsoft.EntityFrameworkCore;

namespace API.Schema.DiscoveryContext;

public class DiscoveryContext(DbContextOptions<DiscoveryContext> options) : KenkuBaseContext<DiscoveryContext>(options)
{
    public DbSet<DiscoveryPost> Posts { get; set; }
}
