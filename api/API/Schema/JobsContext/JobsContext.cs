using Microsoft.EntityFrameworkCore;

namespace API.Schema.JobsContext;

public class JobsContext(DbContextOptions<JobsContext> options) : KenkuBaseContext<JobsContext>(options)
{
    public DbSet<JobRecord> Jobs { get; set; }
    public DbSet<Job> JobQueue { get; set; }
}
