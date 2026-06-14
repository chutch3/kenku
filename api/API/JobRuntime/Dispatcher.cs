using API.JobRuntime.Interfaces;
using API.Schema.JobsContext;
using log4net;

namespace API.JobRuntime;

/// <summary>
/// The runtime's core: claim one ready job, run its handler, and record the outcome — success, bounded
/// retry with backoff, or <see cref="JobStatus.NeedsAttention"/> at the attempt cap. This is where every
/// P1 fix lives (no silent loops, no infinite retry), so its behaviour is pinned by DF1–DF7 under a fake
/// clock.
/// </summary>
public class Dispatcher
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(Dispatcher));
    private readonly IJobStore _store;
    private readonly HandlerRegistry _registry;
    private readonly IClock _clock;
    private readonly BackoffPolicy _backoff;
    private readonly TimeSpan _leaseDuration;
    private readonly int _globalCap;
    private readonly int _perResourceCap;
    private readonly RunningJobRegistry _running;

    public Dispatcher(IJobStore store, HandlerRegistry registry, IClock clock,
        BackoffPolicy? backoff = null, TimeSpan? leaseDuration = null, int globalCap = 8, int perResourceCap = 2,
        RunningJobRegistry? running = null)
    {
        _store = store;
        _registry = registry;
        _clock = clock;
        _backoff = backoff ?? BackoffPolicy.Default;
        _leaseDuration = leaseDuration ?? TimeSpan.FromMinutes(10);
        _globalCap = globalCap;
        _perResourceCap = perResourceCap;
        _running = running ?? new RunningJobRegistry();
    }

    /// <summary>Claims and runs at most one ready job. Returns false when nothing was ready.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct = default)
    {
        Job? job = await _store.ClaimNextReadyAsync(_clock.UtcNow, _leaseDuration, _globalCap, _perResourceCap, ct);
        if (job is null)
            return false;

        IJobHandler? handler = _registry.Resolve(job.Type);
        if (handler is null)
        {
            Fail(job, $"No handler registered for job type '{job.Type}'.", retryable: false);
            await _store.UpdateAsync(job, ct);
            return true;
        }

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            using (_running.Track(job.Key, jobCts))
                await handler.ExecuteAsync(job, jobCts.Token);
            job.Status = JobStatus.Succeeded;
            job.Error = null;
            job.LeasedUntil = null;
            job.FinishedAt = _clock.UtcNow;
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested)
        {
            job.Status = JobStatus.Cancelled;
            job.LeasedUntil = null;
            job.FinishedAt = _clock.UtcNow;
        }
        catch (OperationCanceledException)
        {
            Fail(job, "Timed out waiting for a network request or rate-limit slot.", retryable: true);
        }
        catch (Exception e)
        {
            Fail(job, e.Message, retryable: true);
        }

        await _store.UpdateAsync(job, ct);
        return true;
    }

    private void Fail(Job job, string error, bool retryable)
    {
        job.Error = error;
        job.LeasedUntil = null;
        if (retryable && job.Attempts < job.MaxAttempts)
        {
            job.Status = JobStatus.Queued;
            job.ScheduledFor = _clock.UtcNow + _backoff.Delay(job.Attempts);
            Log.Warn($"Job {job.Key} ({job.Type}) failed (attempt {job.Attempts}/{job.MaxAttempts}), retrying: {error}");
        }
        else
        {
            job.Status = JobStatus.NeedsAttention;
            job.FinishedAt = _clock.UtcNow;
            Log.Error($"Job {job.Key} ({job.Type}) needs attention after {job.Attempts} attempt(s): {error}");
        }
    }
}
