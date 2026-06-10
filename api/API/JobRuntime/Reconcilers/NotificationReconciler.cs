using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.NotificationsContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues a (deduped) <see cref="SendNotificationsHandler"/> job to flush unsent
/// notifications to connectors. Replaces the periodic SendNotificationsWorker.
/// </summary>
public class NotificationReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : Reconciler(scopeFactory, configuration)
{

    public const string DedupKey = "send-notifications";

    protected override TimeSpan Interval => Constants.NotificationSendInterval;

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        ScanAndEnqueueAsync(
            scope.GetRequiredService<NotificationsContext>(),
            scope.GetRequiredService<IJobStore>(),
            clock.UtcNow, ct);

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
