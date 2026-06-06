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
            _jobs.Add(job);
        return Task.FromResult(job);
    }

    public Task<Job?> ClaimNextReadyAsync(DateTime now, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        lock (_lock)
        {
            Job? job = _jobs
                .Where(j => j.Status == JobStatus.Queued && j.ScheduledFor <= now
                            && (j.LeasedUntil is null || j.LeasedUntil <= now))
                .OrderByDescending(j => j.Priority)
                .ThenBy(j => j.CreatedAt)
                .ThenBy(j => j.Key, StringComparer.Ordinal)
                .FirstOrDefault();

            if (job is null)
                return Task.FromResult<Job?>(null);

            job.Status = JobStatus.Running;
            job.Attempts++;
            job.StartedAt ??= now;
            job.LeasedUntil = now + leaseDuration;
            return Task.FromResult<Job?>(job);
        }
    }

    public Task UpdateAsync(Job job, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<Job>>(_jobs.ToList());
    }
}
