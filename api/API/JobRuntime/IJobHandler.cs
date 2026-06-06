using API.Schema.JobsContext;

namespace API.JobRuntime;

/// <summary>
/// The domain logic for one job type. Handlers are pure units of work: they read the job's typed payload,
/// do the work, honour the cancellation token, and throw on failure (the dispatcher records the outcome
/// and decides retry vs <see cref="JobStatus.NeedsAttention"/>). A handler must be idempotent — it may be
/// re-run after a crash or a partial attempt.
/// </summary>
public interface IJobHandler
{
    string JobType { get; }
    Task ExecuteAsync(Job job, CancellationToken ct);
}
