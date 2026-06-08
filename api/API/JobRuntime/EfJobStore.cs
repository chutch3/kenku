using API.JobRuntime.Interfaces;
using API.Schema.JobsContext;
using Microsoft.EntityFrameworkCore;

namespace API.JobRuntime;

/// <summary>
/// EF-backed <see cref="IJobStore"/>. Scoped (one per dispatcher tick). Claim selection reuses
/// <see cref="JobReadySelection"/> so it matches the in-memory store the DF tests pin.
/// </summary>
public class EfJobStore(JobsContext context) : IJobStore
{
    public async Task<Job> EnqueueAsync(Job job, CancellationToken ct = default)
    {
        if (job.DedupKey is { } dedup)
        {
            Job? existing = await context.JobQueue
                .Where(j => j.DedupKey == dedup && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running))
                .FirstOrDefaultAsync(ct);
            if (existing is not null)
                return existing;
        }

        await context.JobQueue.AddAsync(job, ct);
        await context.SaveChangesAsync(ct);
        return job;
    }

    public async Task<Job?> ClaimNextReadyAsync(DateTime now, TimeSpan leaseDuration, int globalCap, int perResourceCap,
        CancellationToken ct = default)
    {
        if (context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
            return await ClaimRelationalAsync(now, leaseDuration, globalCap, perResourceCap, ct);

        List<Job> active = await context.JobQueue
            .Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running)
            .ToListAsync(ct);

        if (JobReadySelection.PickClaimable(active, now, globalCap, perResourceCap) is not { } candidate)
            return null;

        JobReadySelection.MarkClaimed(candidate, now, leaseDuration);
        await context.SaveChangesAsync(ct);
        return candidate;
    }

    private async Task<Job?> ClaimRelationalAsync(DateTime now, TimeSpan leaseDuration, int globalCap, int perResourceCap,
        CancellationToken ct)
    {
        await using var txn = await context.Database.BeginTransactionAsync(ct);
        try
        {
            List<Job> active = await context.JobQueue
                .FromSqlRaw("""
                    SELECT * FROM "JobQueue"
                    WHERE "Status" IN (0, 1)
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);

            if (JobReadySelection.PickClaimable(active, now, globalCap, perResourceCap) is not { } candidate)
            {
                await txn.RollbackAsync(ct);
                return null;
            }

            JobReadySelection.MarkClaimed(candidate, now, leaseDuration);
            await context.SaveChangesAsync(ct);
            await txn.CommitAsync(ct);
            return candidate;
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }

    public async Task UpdateAsync(Job job, CancellationToken ct = default)
    {
        context.JobQueue.Update(job);
        await context.SaveChangesAsync(ct);
    }

    public async Task<Job?> GetAsync(string key, CancellationToken ct = default) =>
        await context.JobQueue.FirstOrDefaultAsync(j => j.Key == key, ct);

    public async Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken ct = default) =>
        await context.JobQueue.AsNoTracking().ToListAsync(ct);

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await context.JobQueue.Where(j => j.Key == key).ExecuteDeleteAsync(ct);
}
