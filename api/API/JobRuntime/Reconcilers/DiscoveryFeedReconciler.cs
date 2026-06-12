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

    public static Task EnqueueAsync(IJobStore store, DateTime now, CancellationToken ct) =>
        store.EnqueueAsync(new Job(RefreshDiscoveryFeedHandler.Type, "{}", now, dedupKey: DedupKey), ct);
}
