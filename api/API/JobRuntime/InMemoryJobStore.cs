using API.Schema.JobsContext;

namespace API.JobRuntime;

/// <summary>
/// In-memory <see cref="IJobStore"/> for the dispatcher test harness. Uses the shared
/// <see cref="JobReadySelection"/> so it claims identically to the EF-backed store (DF1–DF7).
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
                _jobs.FirstOrDefault(j => j.DedupKey == dedup && JobReadySelection.IsActive(j.Status)) is { } existing)
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
            if (JobReadySelection.PickClaimable(_jobs, now, globalCap, perResourceCap) is not { } job)
                return Task.FromResult<Job?>(null);

            JobReadySelection.MarkClaimed(job, now, leaseDuration);
            return Task.FromResult<Job?>(job);
        }
    }

    public Task UpdateAsync(Job job, CancellationToken ct = default) => Task.CompletedTask;

    public Task<Job?> GetAsync(string key, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_jobs.FirstOrDefault(j => j.Key == key));
    }

    public Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<Job>>(_jobs.ToList());
    }
}
