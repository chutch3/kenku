using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using log4net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime;

/// <summary>
/// Periodically (and once at startup) enqueues a single <see cref="VerifyDownloadStateHandler"/> job to
/// reconcile the Downloaded flags with disk. Replaces the UpdateChaptersDownloaded worker; gated on
/// <see cref="Constants.UpdateChaptersDownloadedBeforeStarting"/> and deduped to one outstanding job.
/// </summary>
public class DownloadStateReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DownloadStateReconciler));
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    public const string DedupKey = "verify-download-state";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Kenku:RunStartup", true))
            return;
        if (!Constants.UpdateChaptersDownloadedBeforeStarting)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                await EnqueueAsync(scope.ServiceProvider.GetRequiredService<IJobStore>(), clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("Download-state reconciler error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Enqueues the single deduped verify-download-state job.</summary>
    public static Task EnqueueAsync(IJobStore store, DateTime now, CancellationToken ct) =>
        store.EnqueueAsync(new Job(VerifyDownloadStateHandler.Type, VerifyDownloadStateHandler.Payload(), now,
            dedupKey: DedupKey), ct);
}
