using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues the parameterized <see cref="CleanupHandler"/> jobs (old notifications, stale
/// cover cache, orphaned source-ids). Replaces the individual cleanup workers; deduped per kind.
/// </summary>
public class CleanupReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : Reconciler(scopeFactory, configuration)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    public static string DedupKey(CleanupKind kind) => $"cleanup:{kind}";

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        EnqueueAllAsync(scope.GetRequiredService<IJobStore>(), clock.UtcNow, ct);

    /// <summary>Enqueues one (deduped) Cleanup job per kind.</summary>
    public static async Task EnqueueAllAsync(IJobStore store, DateTime now, CancellationToken ct)
    {
        foreach (CleanupKind kind in Enum.GetValues<CleanupKind>())
        {
            // Orphaned-file deletion is destructive, so the scheduled run is report-only (dry-run);
            // actual deletion is requested explicitly via POST /v2/Maintenance/CleanupOrphanedFiles.
            string payload = kind == CleanupKind.OrphanedFiles
                ? CleanupHandler.PayloadFor(kind, dryRun: true)
                : CleanupHandler.PayloadFor(kind);
            await store.EnqueueAsync(new Job(CleanupHandler.Type, payload, now, dedupKey: DedupKey(kind)), ct);
        }
    }
}
