using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.JobRuntime.Reconcilers;
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
}
