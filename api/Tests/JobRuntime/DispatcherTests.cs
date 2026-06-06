using API.JobRuntime;
using API.Schema.JobsContext;
using Xunit;

namespace API.Tests.JobRuntime;

public class DispatcherTests
{
    private sealed class RecordingHandler(string jobType, List<string> log) : IJobHandler
    {
        public string JobType => jobType;
        public Task ExecuteAsync(Job job, CancellationToken ct)
        {
            log.Add(job.Key);
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysFailsHandler(string jobType) : IJobHandler
    {
        public string JobType => jobType;
        public Task ExecuteAsync(Job job, CancellationToken ct) => throw new InvalidOperationException("nope");
    }

    private static (Dispatcher dispatcher, InMemoryJobStore store, FakeClock clock) Build(
        IEnumerable<IJobHandler> handlers, BackoffPolicy? backoff = null)
    {
        var store = new InMemoryJobStore();
        var clock = new FakeClock();
        var dispatcher = new Dispatcher(store, new HandlerRegistry(handlers), clock, backoff);
        return (dispatcher, store, clock);
    }

    [Fact]
    public async Task RunOnce_WithNoReadyJobs_ReturnsFalse()
    {
        var (dispatcher, _, _) = Build([new RecordingHandler("test", [])]);
        Assert.False(await dispatcher.RunOnceAsync());
    }

    [Fact]
    public async Task RunOnce_RunsQueuedJob_AndMarksItSucceeded()
    {
        var log = new List<string>();
        var (dispatcher, store, clock) = Build([new RecordingHandler("test", log)]);
        var job = await store.EnqueueAsync(new Job("test", "{}", clock.UtcNow));

        Assert.True(await dispatcher.RunOnceAsync());

        Assert.Equal([job.Key], log);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Null(job.LeasedUntil);
        Assert.NotNull(job.FinishedAt);
    }

    [Fact]
    public async Task ClaimSelection_IsDeterministic_PriorityThenFifo()
    {
        var log = new List<string>();
        var (dispatcher, store, clock) = Build([new RecordingHandler("test", log)]);

        var a = await store.EnqueueAsync(new Job("test", "{}", clock.UtcNow, priority: 0));
        clock.Advance(TimeSpan.FromSeconds(1));
        var b = await store.EnqueueAsync(new Job("test", "{}", clock.UtcNow, priority: 10));
        clock.Advance(TimeSpan.FromSeconds(1));
        var c = await store.EnqueueAsync(new Job("test", "{}", clock.UtcNow, priority: 0));

        while (await dispatcher.RunOnceAsync()) { }

        // Highest priority first (b), then the two priority-0 jobs in FIFO order (a before c).
        Assert.Equal([b.Key, a.Key, c.Key], log);
    }

    [Fact]
    public async Task FailingJob_RetriesWithBackoff_AndIsNotReclaimedUntilDue()
    {
        var backoff = new BackoffPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
        var (dispatcher, store, clock) = Build([new AlwaysFailsHandler("fail")], backoff);
        var job = await store.EnqueueAsync(new Job("fail", "{}", clock.UtcNow, maxAttempts: 3));

        Assert.True(await dispatcher.RunOnceAsync());
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal("nope", job.Error);
        Assert.Equal(clock.UtcNow + TimeSpan.FromMinutes(1), job.ScheduledFor);

        // Not due yet → nothing claimed.
        Assert.False(await dispatcher.RunOnceAsync());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await dispatcher.RunOnceAsync());
        Assert.Equal(2, job.Attempts);
        Assert.Equal(clock.UtcNow + TimeSpan.FromMinutes(2), job.ScheduledFor); // exponential
    }

    [Fact]
    public async Task FailingJob_AtAttemptCap_GoesToNeedsAttention_AndStops()
    {
        var backoff = new BackoffPolicy(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0));
        var (dispatcher, store, clock) = Build([new AlwaysFailsHandler("fail")], backoff);
        var job = await store.EnqueueAsync(new Job("fail", "{}", clock.UtcNow, maxAttempts: 3));

        for (int i = 0; i < 3; i++)
            Assert.True(await dispatcher.RunOnceAsync());

        Assert.Equal(JobStatus.NeedsAttention, job.Status);
        Assert.Equal(3, job.Attempts);
        // No infinite loop: a NeedsAttention job is never re-claimed.
        Assert.False(await dispatcher.RunOnceAsync());
    }

    [Fact]
    public async Task RunOnce_WhenHandlerHonoursCancellation_MarksJobCancelled()
    {
        var store = new InMemoryJobStore();
        var clock = new FakeClock();
        var handler = new CancelObservingHandler("cancelme");
        var dispatcher = new Dispatcher(store, new HandlerRegistry([handler]), clock);
        var job = await store.EnqueueAsync(new Job("cancelme", "{}", clock.UtcNow));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Assert.True(await dispatcher.RunOnceAsync(cts.Token));

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Null(job.LeasedUntil);
    }

    private sealed class CancelObservingHandler(string jobType) : IJobHandler
    {
        public string JobType => jobType;
        public Task ExecuteAsync(Job job, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task UnknownJobType_GoesToNeedsAttention()
    {
        var (dispatcher, store, clock) = Build([new RecordingHandler("test", [])]);
        var job = await store.EnqueueAsync(new Job("ghost", "{}", clock.UtcNow));

        Assert.True(await dispatcher.RunOnceAsync());

        Assert.Equal(JobStatus.NeedsAttention, job.Status);
        Assert.Contains("ghost", job.Error);
    }
}
