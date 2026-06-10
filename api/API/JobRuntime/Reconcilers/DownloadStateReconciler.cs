using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically (and once at startup) enqueues a single <see cref="VerifyDownloadStateHandler"/> job to
/// reconcile the Downloaded flags with disk. Replaces the UpdateChaptersDownloaded worker; gated on
/// <see cref="Constants.UpdateChaptersDownloadedBeforeStarting"/> and deduped to one outstanding job.
/// </summary>
public class DownloadStateReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : Reconciler(scopeFactory, configuration)
{
    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    public const string DedupKey = "verify-download-state";

    protected override bool Enabled => Constants.UpdateChaptersDownloadedBeforeStarting;

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        EnqueueAsync(scope.GetRequiredService<IJobStore>(), clock.UtcNow, ct);

    /// <summary>Enqueues the single deduped verify-download-state job.</summary>
    public static Task EnqueueAsync(IJobStore store, DateTime now, CancellationToken ct) =>
        store.EnqueueAsync(new Job(VerifyDownloadStateHandler.Type, VerifyDownloadStateHandler.Payload(), now,
            dedupKey: DedupKey), ct);
}
