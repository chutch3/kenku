using Microsoft.EntityFrameworkCore;

namespace API.Schema.JobsContext;

public class JobsContext(DbContextOptions<JobsContext> options) : KenkuBaseContext<JobsContext>(options)
{
    public DbSet<Job> JobQueue { get; set; }
}
