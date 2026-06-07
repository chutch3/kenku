using API.Schema.JobsContext;

namespace API.JobRuntime.Interfaces;

/// <summary>
/// Persistence seam for the job queue. An in-memory implementation backs the dispatcher's unit tests
/// (DF1–DF7) under a fake clock; an EF-backed implementation backs production.
/// </summary>
public interface IJobStore
{
    /// <summary>
    /// Enqueues a job, coalescing on <see cref="Job.DedupKey"/>: if an active (Queued/Running) job already
    /// has the same dedup key, that existing job is returned instead of adding a duplicate (DF5).
    /// </summary>
    Task<Job> EnqueueAsync(Job job, CancellationToken ct = default);

    /// <summary>
    /// Atomically claims the single highest-priority ready job, marking it Running and leasing it until
    /// <c>now + leaseDuration</c>. Ready = Queued and due (<c>ScheduledFor &lt;= now</c>), OR Running with an
    /// expired lease (a crashed worker, DF4). Respects the global concurrency cap and the per-resource cap
    /// (DF2 fairness, DF7) — a resource at its cap is skipped so other resources are not starved. Returns
    /// null when nothing is claimable.
    /// </summary>
    Task<Job?> ClaimNextReadyAsync(DateTime now, TimeSpan leaseDuration, int globalCap, int perResourceCap,
        CancellationToken ct = default);

    Task UpdateAsync(Job job, CancellationToken ct = default);

    Task<Job?> GetAsync(string key, CancellationToken ct = default);

    Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken ct = default);
}
