using API.JobRuntime;
using API.Schema.JobsContext;
using Xunit;

namespace API.Tests.Unit.JobRuntime;

public class JobStoreTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    private static Job NewJob(DateTime createdAt, string? resourceKey = null, string? dedupKey = null, int priority = 0)
        => new("test", "{}", createdAt, resourceKey, dedupKey, priority);

    [Fact]
    public async Task Enqueue_CoalescesOnDedupKey_WhileAnActiveJobExists()
    {
        var store = new InMemoryJobStore();
        var first = await store.EnqueueAsync(NewJob(Now, dedupKey: "sync-series-1"));
        var second = await store.EnqueueAsync(NewJob(Now, dedupKey: "sync-series-1"));

        Assert.Same(first, second);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task Enqueue_DoesNotCoalesce_OnceThePriorJobIsTerminal()
    {
        var store = new InMemoryJobStore();
        var first = await store.EnqueueAsync(NewJob(Now, dedupKey: "d"));
        first.Status = JobStatus.Succeeded;

        var second = await store.EnqueueAsync(NewJob(Now, dedupKey: "d"));

        Assert.NotSame(first, second);
        Assert.Equal(2, (await store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Claim_HonoursTheGlobalConcurrencyCap()
    {
        var store = new InMemoryJobStore();
        await store.EnqueueAsync(NewJob(Now));
        await store.EnqueueAsync(NewJob(Now));
        await store.EnqueueAsync(NewJob(Now));

        Assert.NotNull(await store.ClaimNextReadyAsync(Now, Lease, globalCap: 2, perResourceCap: 10));
        Assert.NotNull(await store.ClaimNextReadyAsync(Now, Lease, globalCap: 2, perResourceCap: 10));
        Assert.Null(await store.ClaimNextReadyAsync(Now, Lease, globalCap: 2, perResourceCap: 10));
    }

    [Fact]
    public async Task Claim_HonoursPerResourceCap_AndDoesNotStarveOtherResources()
    {
        var store = new InMemoryJobStore();
        await store.EnqueueAsync(NewJob(Now, resourceKey: "A", priority: 10));
        await store.EnqueueAsync(NewJob(Now, resourceKey: "A", priority: 10));
        await store.EnqueueAsync(NewJob(Now, resourceKey: "B"));

        var first = await store.ClaimNextReadyAsync(Now, Lease, globalCap: 10, perResourceCap: 1);
        var second = await store.ClaimNextReadyAsync(Now, Lease, globalCap: 10, perResourceCap: 1);

        // A is capped at one in-flight, so the lower-priority B is claimed rather than starved.
        Assert.Equal("A", first!.ResourceKey);
        Assert.Equal("B", second!.ResourceKey);
        // Both resources now at cap → the second A job is not claimable.
        Assert.Null(await store.ClaimNextReadyAsync(Now, Lease, globalCap: 10, perResourceCap: 1));
    }

    [Fact]
    public async Task Claim_ReleasesACrashedJob_OnlyAfterItsLeaseExpires()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(Now));

        var claimed = await store.ClaimNextReadyAsync(Now, Lease, globalCap: 10, perResourceCap: 10);
        Assert.Same(job, claimed);
        Assert.Equal(1, job.Attempts);

        // Lease still active → not re-claimed (no double-run).
        Assert.Null(await store.ClaimNextReadyAsync(Now.AddMinutes(5), Lease, globalCap: 10, perResourceCap: 10));

        // Lease expired (worker presumed dead) → re-leased exactly once.
        var reclaimed = await store.ClaimNextReadyAsync(Now.AddMinutes(11), Lease, globalCap: 10, perResourceCap: 10);
        Assert.Same(job, reclaimed);
        Assert.Equal(2, job.Attempts);
    }

    [Fact]
    public async Task Claim_NeverReleasesASucceededJob()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(NewJob(Now));
        await store.ClaimNextReadyAsync(Now, Lease, globalCap: 10, perResourceCap: 10);
        job.Status = JobStatus.Succeeded;

        Assert.Null(await store.ClaimNextReadyAsync(Now.AddHours(1), Lease, globalCap: 10, perResourceCap: 10));
    }
}
