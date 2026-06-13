using API.JobRuntime.Handlers;
using API.JobRuntime.Interfaces;
using API.Schema.JobsContext;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Hourly tick that enqueues the (deduped) <see cref="RefreshDiscoveryFeedHandler"/> job keeping the
/// Discover page's feed rails cached in the database.
/// </summary>
public class DiscoveryFeedReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : Reconciler(scopeFactory, configuration)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    public const string DedupKey = "discovery-feed";

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        EnqueueAsync(scope.GetRequiredService<IJobStore>(), clock.UtcNow, ct);

    public static async Task EnqueueAsync(IJobStore store, DateTime now, CancellationToken ct)
    {
        // A periodic refresh must not stay wedged: enqueue dedups onto an active job, and a parked
        // (NeedsAttention) run counts as active — so a single past hard failure would disable the feed
        // forever. Re-arm that parked job instead so the next tick runs it.
        if ((await store.GetAllAsync(ct)).FirstOrDefault(j =>
                j.DedupKey == DedupKey && j.Status == JobStatus.NeedsAttention) is { } parked)
        {
            parked.Status = JobStatus.Queued;
            parked.Attempts = 0;
            parked.ScheduledFor = now;
            parked.Error = null;
            await store.UpdateAsync(parked, ct);
            return;
        }

        await store.EnqueueAsync(new Job(RefreshDiscoveryFeedHandler.Type, "{}", now, dedupKey: DedupKey), ct);
    }
}
