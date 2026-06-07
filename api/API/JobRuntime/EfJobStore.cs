using API.JobRuntime.Interfaces;
using API.Schema.JobsContext;
using Microsoft.EntityFrameworkCore;

namespace API.JobRuntime;

/// <summary>
/// EF-backed <see cref="IJobStore"/>. Scoped (one per dispatcher tick). Claim selection reuses
/// <see cref="JobReadySelection"/> so it matches the in-memory store the DF tests pin.
/// </summary>
/// <remarks>
/// The claim loads the active/ready set and selects in memory. Under high concurrency two ticks could
/// race the same row; hardening the claim with <c>SELECT … FOR UPDATE SKIP LOCKED</c> is deferred to the
/// first real handler (step 2), where it is verified against Postgres. The runtime is not wired to real
/// work yet, so no production claim race is exercised in step 1.
/// </remarks>
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
        List<Job> active = await context.JobQueue
            .Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running)
            .ToListAsync(ct);

        if (JobReadySelection.PickClaimable(active, now, globalCap, perResourceCap) is not { } job)
            return null;

        JobReadySelection.MarkClaimed(job, now, leaseDuration);
        await context.SaveChangesAsync(ct);
        return job;
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
}
