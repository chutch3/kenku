using API.JobRuntime.Interfaces;
using API.Schema.JobsContext;
using API.Schema.NotificationsContext;
using API.Schema.NotificationsContext.NotificationConnectors;
using log4net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>
/// Fans unsent notifications out to the configured connectors, bundling by title and holding a group until
/// it has settled (no new notification for 2× the send interval). Replaces SendNotificationsWorker.
/// </summary>
public class SendNotificationsHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(SendNotificationsHandler));

    public const string Type = "SendNotifications";
    public string JobType => Type;

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        NotificationsContext context = scope.ServiceProvider.GetRequiredService<NotificationsContext>();

        if (await context.Notifications.Where(n => !n.IsSent).ToListAsync(ct) is not { Count: > 0 } unsent)
            return;

        List<NotificationConnector> connectors = await context.NotificationConnectors.ToListAsync(ct);
        Log.DebugFormat("Sending {0} notifications to {1} connectors...", unsent.Count, connectors.Count);

        foreach (var group in unsent.GroupBy(n => n.Title, n => n).Select(g => new { Title = g.Key, Notifications = g.ToList() }))
        {
            if (group.Notifications.MaxBy(n => n.Date)!.Date > DateTime.UtcNow.Subtract(Constants.NotificationSendInterval * 2))
            {
                Log.DebugFormat("Holding notification {0} for bundling.", group.Title);
                continue;
            }
            connectors.ForEach(c => c.SendNotification(group.Title, string.Join(", ", group.Notifications.Select(n => n.Message))));
            group.Notifications.ForEach(n => n.IsSent = true);
        }

        if (await context.Sync(ct, typeof(SendNotificationsHandler), nameof(ExecuteAsync)) is { success: false } e)
            Log.ErrorFormat("Failed to save database changes: {0}", e.exceptionMessage);
    }
}
