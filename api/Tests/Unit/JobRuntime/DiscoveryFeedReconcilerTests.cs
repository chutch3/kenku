using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.JobRuntime.Reconcilers;
using API.Schema.JobsContext;
using Xunit;

namespace API.Tests.Unit.JobRuntime;

/// <summary>Hourly tick that enqueues the (deduped) discovery feed refresh job.</summary>
public class DiscoveryFeedReconcilerTests
{
    [Fact]
    public async Task Tick_EnqueuesOneDedupedRefreshJob()
    {
        var store = new InMemoryJobStore();

        await DiscoveryFeedReconciler.EnqueueAsync(store, DateTime.UtcNow, default);
        await DiscoveryFeedReconciler.EnqueueAsync(store, DateTime.UtcNow, default);

        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(RefreshDiscoveryFeedHandler.Type, job.Type);
        Assert.Equal(DiscoveryFeedReconciler.DedupKey, job.DedupKey);
    }

    [Fact]
    public async Task Tick_ReArmsAParkedRefreshJob_SoAHardFailureCannotWedgeTheFeedForever()
    {
        var store = new InMemoryJobStore();
        // A prior run failed its way to NeedsAttention; dedup would otherwise coalesce onto it forever.
        var parked = await store.EnqueueAsync(new Job(RefreshDiscoveryFeedHandler.Type, "{}", DateTime.UtcNow,
            dedupKey: DiscoveryFeedReconciler.DedupKey));
        parked.Status = JobStatus.NeedsAttention;
        parked.Attempts = 5;
        parked.Error = "boom";
        await store.UpdateAsync(parked);

        await DiscoveryFeedReconciler.EnqueueAsync(store, DateTime.UtcNow, default);

        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.Error);
    }
}
