using API.Schema.JobsContext;

namespace API.JobRuntime;

/// <summary>
/// Persistence seam for the job queue. An in-memory implementation backs the dispatcher's unit tests
/// (DF1–DF7) under a fake clock; an EF-backed implementation backs production.
/// </summary>
public interface IJobStore
{
    Task<Job> EnqueueAsync(Job job, CancellationToken ct = default);

    /// <summary>
    /// Atomically claims the single highest-priority ready job — Queued, due (<c>ScheduledFor &lt;= now</c>),
    /// and not currently leased — marking it Running and leasing it until <c>now + leaseDuration</c>.
    /// Returns null when nothing is ready.
    /// </summary>
    Task<Job?> ClaimNextReadyAsync(DateTime now, TimeSpan leaseDuration, CancellationToken ct = default);

    Task UpdateAsync(Job job, CancellationToken ct = default);

    Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken ct = default);
}
