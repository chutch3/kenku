using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using log4net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues a (deduped) <see cref="SendNotificationsHandler"/> job to flush unsent
/// notifications to connectors. Replaces the periodic SendNotificationsWorker.
/// </summary>
public class NotificationReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(NotificationReconciler));

    public const string DedupKey = "send-notifications";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Kenku:RunStartup", true))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                    new Job(SendNotificationsHandler.Type, "{}", clock.UtcNow, dedupKey: DedupKey), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("Notification reconciler error", e); }

            await Task.Delay(Constants.NotificationSendInterval, stoppingToken);
        }
    }
}
