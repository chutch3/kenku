using API.Schema.JobsContext;
using log4net;
using Microsoft.Extensions.DependencyInjection;

namespace API.Workers;

/// <summary>
/// Persists worker-execution status to the <see cref="JobsContext"/> so job state survives restarts and
/// repeated runs are observable. The worker queue is the single call site, so existing workers need no
/// changes. Recording never throws into the worker path — a recording failure is logged and swallowed.
/// </summary>
public interface IJobRecorder
{
    Task RecordStartedAsync(BaseWorker worker);
    Task RecordFinishedAsync(BaseWorker worker, WorkerExecutionState state, string? error);
}

public class JobRecorder(IServiceScopeFactory scopeFactory) : IJobRecorder
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(JobRecorder));

    public Task RecordStartedAsync(BaseWorker worker) => Upsert(worker, WorkerExecutionState.Running, null, finished: false);

    public Task RecordFinishedAsync(BaseWorker worker, WorkerExecutionState state, string? error) =>
        Upsert(worker, state, error, finished: true);

    private static bool IsTerminal(WorkerExecutionState state) =>
        state is WorkerExecutionState.Completed or WorkerExecutionState.Failed or WorkerExecutionState.Cancelled;

    private async Task Upsert(BaseWorker worker, WorkerExecutionState state, string? error, bool finished)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            JobsContext context = scope.ServiceProvider.GetRequiredService<JobsContext>();
            DateTime now = DateTime.UtcNow;

            if (await context.Jobs.FindAsync(worker.Key) is { } existing)
            {
                // A late "started" must never downgrade a record the finish already marked terminal.
                if (!finished && IsTerminal(existing.State))
                    return;
                existing.State = state;
                existing.Name = worker.ToString() ?? existing.Name;
                existing.Error = error;
                if (finished) existing.FinishedAt = now; else existing.StartedAt = now;
            }
            else
            {
                JobRecord record = new(worker.Key, worker.GetType().Name, worker.ToString() ?? worker.GetType().Name, now)
                {
                    State = state,
                    Error = error,
                    StartedAt = finished ? null : now,
                    FinishedAt = finished ? now : null
                };
                await context.Jobs.AddAsync(record);
            }

            await context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            Log.ErrorFormat("Failed to record job status for {0}: {1}", worker, e);
        }
    }
}
