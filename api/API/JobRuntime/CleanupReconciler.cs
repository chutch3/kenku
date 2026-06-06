using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Services;
using log4net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime;

/// <summary>
/// Periodically enqueues the parameterized <see cref="CleanupHandler"/> jobs (old notifications, stale
/// cover cache, orphaned source-ids). Replaces the individual cleanup workers; deduped per kind.
/// </summary>
public class CleanupReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(CleanupReconciler));
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public static string DedupKey(CleanupKind kind) => $"cleanup:{kind}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Kenku:RunStartup", true))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                await EnqueueAllAsync(scope.ServiceProvider.GetRequiredService<IJobStore>(), clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("Cleanup reconciler error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Enqueues one (deduped) Cleanup job per kind.</summary>
    public static async Task EnqueueAllAsync(IJobStore store, DateTime now, CancellationToken ct)
    {
        foreach (CleanupKind kind in Enum.GetValues<CleanupKind>())
            await store.EnqueueAsync(new Job(CleanupHandler.Type, CleanupHandler.PayloadFor(kind), now,
                dedupKey: DedupKey(kind)), ct);
    }
}
