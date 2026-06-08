using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.JobRuntime.Reconcilers;
using API.Schema.NotificationsContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Unit.JobRuntime;

/// <summary>
/// The notification reconciler must only enqueue a SendNotifications job when there is actually something
/// to send — otherwise it created a no-op job every tick (1/min), flooding the queue with 1000+ jobs/day.
/// </summary>
public class NotificationReconcilerTests : IDisposable
{
    private readonly NotificationsContext _ctx = new(new DbContextOptionsBuilder<NotificationsContext>()
        .UseInMemoryDatabase("notif-rec-" + Guid.NewGuid().ToString("N")).Options);

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public async Task Scan_EnqueuesSendNotifications_WhenUnsentNotificationsExist()
    {
        _ctx.Notifications.Add(new Notification("New chapter", "Ch. 1"));
        await _ctx.SaveChangesAsync();
        var store = new InMemoryJobStore();

        await NotificationReconciler.ScanAndEnqueueAsync(_ctx, store, DateTime.UtcNow, default);

        var jobs = await store.GetAllAsync();
        Assert.Single(jobs);
        Assert.Equal(SendNotificationsHandler.Type, jobs[0].Type);
    }

    [Fact]
    public async Task Scan_DoesNotEnqueue_WhenNoUnsentNotifications()
    {
        // A previously-sent notification must not keep the reconciler enqueuing no-op jobs.
        _ctx.Notifications.Add(new Notification("k", "Sent", "msg", NotificationUrgency.Normal, DateTime.UtcNow, isSent: true));
        await _ctx.SaveChangesAsync();
        var store = new InMemoryJobStore();

        await NotificationReconciler.ScanAndEnqueueAsync(_ctx, store, DateTime.UtcNow, default);

        Assert.Empty(await store.GetAllAsync());
    }
}
