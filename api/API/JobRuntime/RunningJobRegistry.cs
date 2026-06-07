using System.Collections.Concurrent;

namespace API.JobRuntime;

/// <summary>
/// Tracks the cancellation source of each in-flight job so a cancel request can cooperatively stop a
/// running handler (DF6/AF6d). The dispatcher registers a job while it runs; the cancel endpoint signals
/// the token, and the handler — honouring it — throws, which the dispatcher records as Cancelled.
/// </summary>
public class RunningJobRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    public IDisposable Track(string jobKey, CancellationTokenSource cts)
    {
        _running[jobKey] = cts;
        return new Tracker(this, jobKey);
    }

    /// <summary>Signals the running job's token. Returns false if the job isn't running in this process.</summary>
    public bool Cancel(string jobKey)
    {
        if (!_running.TryGetValue(jobKey, out CancellationTokenSource? cts))
            return false;
        cts.Cancel();
        return true;
    }

    private sealed class Tracker(RunningJobRegistry registry, string jobKey) : IDisposable
    {
        public void Dispose() => registry._running.TryRemove(jobKey, out _);
    }
}
