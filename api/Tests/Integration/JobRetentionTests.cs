using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Services;
using API.Tests.Unit.JobRuntime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    // The handler must honour the runtime CompletedJobRetentionDays setting, not a compile-time
    // default: with retention shortened to 1 day, a 2-day-old job (safe under the default 3) is pruned.
    [Fact]
    public async Task CleanupHandler_PrunesByTheConfiguredRetention()
    {
        var now = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);
        var twoDaysOld = now.AddDays(-2);

        await using (var seed = NewContext())
        {
            seed.JobQueue.AddRange(
                Job(JobStatus.Succeeded, twoDaysOld, twoDaysOld), // pruned only if the 1-day setting is honoured
                Job(JobStatus.Succeeded, now.AddHours(-1), now.AddHours(-1)));
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());
        services.AddScoped<CleanupService>();
        services.AddSingleton<API.JobRuntime.Interfaces.IClock>(new FakeClock(now));
        services.AddSingleton(new KenkuSettings { CompletedJobRetentionDays = 1 });
        await using var provider = services.BuildServiceProvider();

        var handler = new CleanupHandler(provider.GetRequiredService<IServiceScopeFactory>());
        await handler.ExecuteAsync(
            new JobEntity(CleanupHandler.Type, CleanupHandler.PayloadFor(CleanupKind.CompletedJobs), now),
            CancellationToken.None);

        await using var verify = NewContext();
        var remaining = Assert.Single(await verify.JobQueue.ToListAsync());
        Assert.Equal(now.AddHours(-1), remaining.FinishedAt);
    }
}
