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
        bool isRelational = context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";

        List<Job> active = await context.JobQueue
            .Where(j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running)
            .AsNoTracking()
            .ToListAsync(ct);

        if (JobReadySelection.PickClaimable(active, now, globalCap, perResourceCap) is not { } candidate)
            return null;

        DateTime claimedUntil = now.Add(leaseDuration);

        if (isRelational)
        {
            int updated = await context.JobQueue
                .Where(j => j.Key == candidate.Key && j.Status == JobStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Running)
                    .SetProperty(j => j.ScheduledFor, claimedUntil), ct);

            if (updated == 0)
                return null;

            candidate.Status = JobStatus.Running;
            candidate.ScheduledFor = claimedUntil;
            return candidate;
        }

        // InMemory provider (tests): reload as tracked and mutate via SaveChanges.
        Job? tracked = await context.JobQueue
            .Where(j => j.Key == candidate.Key && j.Status == JobStatus.Queued)
            .FirstOrDefaultAsync(ct);

        if (tracked is null)
            return null;

        JobReadySelection.MarkClaimed(tracked, now, leaseDuration);
        await context.SaveChangesAsync(ct);
        return tracked;
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
