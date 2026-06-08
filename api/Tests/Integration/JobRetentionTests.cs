using API.Schema.JobsContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

/// <summary>
/// Retention: completed (Succeeded/Cancelled) jobs older than the cutoff are pruned so the JobQueue table
/// doesn't grow without bound, while recent jobs and jobs needing eyes (Failed/NeedsAttention) are kept.
/// Postgres-only: cleanup uses ExecuteDeleteAsync, which the InMemory provider does not support.
/// </summary>
public class JobRetentionTests : IAsyncLifetime
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

    public async Task DisposeAsync() => await _postgres.DropDatabaseAsync(_dbName);

    private JobsContext NewContext() => new(new DbContextOptionsBuilder<JobsContext>().UseNpgsql(_cs).Options);

    private static JobEntity Job(JobStatus status, DateTime created, DateTime? finished)
    {
        var job = new JobEntity("CountingJob", "{}", created) { Status = status };
        job.FinishedAt = finished;
        return job;
    }

    [Fact]
    public async Task CleanupCompletedJobs_RemovesOldSucceededAndCancelled_KeepsEverythingElse()
    {
        var now = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);
        var old = now.AddDays(-4);   // older than the 3-day retention
        var recent = now.AddHours(-1);

        await using (var seed = NewContext())
        {
            seed.JobQueue.AddRange(
                Job(JobStatus.Succeeded, old, old),       // pruned
                Job(JobStatus.Cancelled, old, old),       // pruned
                Job(JobStatus.Succeeded, recent, recent), // kept — recent
                Job(JobStatus.NeedsAttention, old, old),  // kept — needs eyes regardless of age
                Job(JobStatus.Failed, old, old),          // kept — needs eyes regardless of age
                Job(JobStatus.Queued, old, null));        // kept — still active
            await seed.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
            await new CleanupService().CleanupCompletedJobsAsync(ctx, now, TimeSpan.FromDays(3), CancellationToken.None);

        await using (var verify = NewContext())
        {
            var remaining = await verify.JobQueue.ToListAsync();
            Assert.Equal(4, remaining.Count);
            Assert.DoesNotContain(remaining, j => j.Status == JobStatus.Succeeded && j.FinishedAt == old);
            Assert.DoesNotContain(remaining, j => j.Status == JobStatus.Cancelled);
            Assert.Contains(remaining, j => j.Status == JobStatus.Succeeded && j.FinishedAt == recent);
            Assert.Contains(remaining, j => j.Status == JobStatus.NeedsAttention);
            Assert.Contains(remaining, j => j.Status == JobStatus.Failed);
            Assert.Contains(remaining, j => j.Status == JobStatus.Queued);
        }
    }
}
