using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.NotificationsContext;
using log4net;
using Microsoft.EntityFrameworkCore;
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
                await ScanAndEnqueueAsync(
                    scope.ServiceProvider.GetRequiredService<NotificationsContext>(),
                    scope.ServiceProvider.GetRequiredService<IJobStore>(),
                    clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("Notification reconciler error", e); }

            await Task.Delay(Constants.NotificationSendInterval, stoppingToken);
        }
    }

    /// <summary>Enqueues a (deduped) SendNotifications job only when notifications are actually waiting to be
    /// sent — an unconditional tick created a no-op job every interval, flooding the queue.</summary>
    public static async Task<int> ScanAndEnqueueAsync(NotificationsContext notifications, IJobStore store, DateTime now, CancellationToken ct)
    {
        if (!await notifications.Notifications.AnyAsync(n => !n.IsSent, ct))
            return 0;

        await store.EnqueueAsync(new Job(SendNotificationsHandler.Type, "{}", now, dedupKey: DedupKey), ct);
        return 1;
    }
}
