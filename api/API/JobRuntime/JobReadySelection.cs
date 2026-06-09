using API.Schema.JobsContext;

namespace API.JobRuntime;

/// <summary>
/// The claim decision, shared by every <see cref="IJobStore"/> so in-memory and EF-backed stores select
/// identically (DF1–DF7). Ready = Queued and due, OR Running with an expired lease (crash recovery).
/// Selection is priority → FIFO → key, skipping resources already at their cap and stopping at the global
/// cap so no resource is starved.
/// </summary>
public static class JobReadySelection
{
    public static Job? PickClaimable(IReadOnlyCollection<Job> jobs, DateTime now, int globalCap, int perResourceCap)
    {
        if (jobs.Count(LeaseActive) >= globalCap)
            return null;

        return jobs
            .Where(j => (j.Status == JobStatus.Queued && j.ScheduledFor <= now)
                        || (j.Status == JobStatus.Running && j.LeasedUntil <= now))
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.CreatedAt)
            .ThenBy(j => j.Key, StringComparer.Ordinal)
            .FirstOrDefault(j => j.ResourceKey is null
                                 || jobs.Count(o => o.ResourceKey == j.ResourceKey && LeaseActive(o)) < perResourceCap);

        bool LeaseActive(Job j) => j.Status == JobStatus.Running && j.LeasedUntil > now;
    }

    public static void MarkClaimed(Job job, DateTime now, TimeSpan leaseDuration)
    {
        job.Status = JobStatus.Running;
        job.Attempts++;
        job.StartedAt ??= now;
        job.LeasedUntil = now + leaseDuration;
    }

    /// <summary>A job that a new enqueue with the same dedup key should coalesce onto. NeedsAttention
    /// counts: it means "stop and wait for the user", so automated re-enqueues must not pile up fresh
    /// duplicates behind it — a new job only spawns once the user retries or dismisses it.</summary>
    public static bool IsActive(JobStatus status) =>
        status is JobStatus.Queued or JobStatus.Running or JobStatus.NeedsAttention;
}
