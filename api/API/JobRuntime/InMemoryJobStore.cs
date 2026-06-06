using API.Schema.JobsContext;

namespace API.JobRuntime;

/// <summary>
/// In-memory <see cref="IJobStore"/> for the dispatcher test harness. Claim selection is deterministic
/// (priority, then FIFO by CreatedAt, then Key) so DF1 is provable under a fake clock.
/// </summary>
public class InMemoryJobStore : IJobStore
{
    private readonly object _lock = new();
    private readonly List<Job> _jobs = new();

    public Task<Job> EnqueueAsync(Job job, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (job.DedupKey is { } dedup &&
                _jobs.FirstOrDefault(j => j.DedupKey == dedup && IsActive(j.Status)) is { } existing)
                return Task.FromResult(existing);

            _jobs.Add(job);
        }
        return Task.FromResult(job);
    }

    public Task<Job?> ClaimNextReadyAsync(DateTime now, TimeSpan leaseDuration, int globalCap, int perResourceCap,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            bool IsLeaseActive(Job j) => j.Status == JobStatus.Running && j.LeasedUntil > now;

            if (_jobs.Count(IsLeaseActive) >= globalCap)
                return Task.FromResult<Job?>(null);

            Job? job = _jobs
                .Where(j => (j.Status == JobStatus.Queued && j.ScheduledFor <= now)
                            || (j.Status == JobStatus.Running && j.LeasedUntil <= now))
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.CreatedAt)
                .ThenBy(j => j.Key, StringComparer.Ordinal)
                .FirstOrDefault(j => j.ResourceKey is null
                                     || _jobs.Count(o => o.ResourceKey == j.ResourceKey && IsLeaseActive(o)) < perResourceCap);

            if (job is null)
                return Task.FromResult<Job?>(null);

            job.Status = JobStatus.Running;
            job.Attempts++;
            job.StartedAt ??= now;
            job.LeasedUntil = now + leaseDuration;
            return Task.FromResult<Job?>(job);
        }
    }

    private static bool IsActive(JobStatus status) => status is JobStatus.Queued or JobStatus.Running;

    public Task UpdateAsync(Job job, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<Job>>(_jobs.ToList());
    }
}
