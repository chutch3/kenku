using API.JobRuntime.Reconcilers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Services;
using Xunit;

namespace API.Tests.JobRuntime;

/// <summary>
/// The reconciler that replaced the individual cleanup workers: it enqueues one parameterized Cleanup job
/// per <see cref="CleanupKind"/>, deduped so ticks coalesce.
/// </summary>
public class CleanupReconcilerTests
{
    [Fact]
    public async Task EnqueueAll_EnqueuesOneJobPerCleanupKind()
    {
        var store = new InMemoryJobStore();

        await CleanupReconciler.EnqueueAllAsync(store, DateTime.UtcNow, default);

        var jobs = await store.GetAllAsync();
        Assert.Equal(Enum.GetValues<CleanupKind>().Length, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(CleanupHandler.Type, j.Type));
    }

    [Fact]
    public async Task EnqueueAll_IsDeduped_SoTicksDoNotPileUp()
    {
        var store = new InMemoryJobStore();

        await CleanupReconciler.EnqueueAllAsync(store, DateTime.UtcNow, default);
        await CleanupReconciler.EnqueueAllAsync(store, DateTime.UtcNow, default);

        Assert.Equal(Enum.GetValues<CleanupKind>().Length, (await store.GetAllAsync()).Count);
    }
}
