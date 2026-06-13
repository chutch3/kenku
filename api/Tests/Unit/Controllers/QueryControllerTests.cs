using API.Controllers;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.NotificationsContext;
using API.Schema.SeriesContext;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Unit.Controllers;

public class QueryControllerTests
{
    private static T NewContext<T>() where T : DbContext =>
        (T)Activator.CreateInstance(typeof(T), new DbContextOptionsBuilder<T>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)!;

    [Fact]
    public async Task GetStats_CountsAcrossTheSeriesNotificationsAndActionsContexts()
    {
        var series = NewContext<SeriesContext>();
        var library = new FileLibrary("/tmp", "Lib");
        var a = new Series("Berserk", "", "", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        var b = new Series("Saga", "", "", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        series.Series.AddRange(a, b);
        series.Chapters.AddRange(new Chapter(a, "1", null, null) { Downloaded = true }, new Chapter(a, "2", null, null));
        await series.SaveChangesAsync();

        var notifications = NewContext<NotificationsContext>();
        notifications.Notifications.AddRange(new Notification("Sent") { IsSent = true }, new Notification("Pending"));
        await notifications.SaveChangesAsync();

        var actions = NewContext<ActionsContext>();
        actions.Actions.Add(new StartupActionRecord());
        await actions.SaveChangesAsync();

        var controller = new QueryController(series, notifications, actions)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var ok = await controller.GetStats();

        var stats = ok.Value!;
        Assert.Equal(2, stats.NumberManga);
        Assert.Equal(2, stats.NumberChapters);
        Assert.Equal(1, stats.DownloadedChapters);
        Assert.Equal(1, stats.MissingChapters);
        Assert.Equal(1, stats.SentNotifications);
        Assert.Equal(1, stats.ActionsTaken);
    }
}
