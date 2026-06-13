using System.Net.Http.Json;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.NotificationsContext;
using API.Schema.SeriesContext;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Stats = API.Controllers.DTOs.Stats;

namespace API.Tests.Integration;

/// <summary>
/// The dashboard's server-stats endpoint through the real container: it aggregates counts across the
/// series, notifications, and actions databases and must return them for the populated library.
/// </summary>
public class QueryStatsEndToEndTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private readonly string _libDir = Path.Combine(Path.GetTempPath(), "kenku-stats-" + Guid.NewGuid().ToString("N"));
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        Directory.CreateDirectory(_libDir);
        _app = new KenkuApplicationFactory { PostgresConnectionString = _postgres.GetConnectionString(_dbName) };

        using var scope = _app.Services.CreateScope();
        var series = scope.ServiceProvider.GetRequiredService<SeriesContext>();
        var library = new FileLibrary(_libDir, "Lib");
        series.FileLibraries.Add(library);
        var a = new Series("Berserk", "", "", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        var b = new Series("Saga", "", "", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        series.Series.AddRange(a, b);
        var done = new Chapter(a, "1", null, null) { Downloaded = true };
        series.Chapters.AddRange(done, new Chapter(a, "2", null, null));
        await series.SaveChangesAsync();

        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsContext>();
        notifications.Notifications.Add(new Notification("Done") { IsSent = true });
        await notifications.SaveChangesAsync();

        var actions = scope.ServiceProvider.GetRequiredService<ActionsContext>();
        actions.Actions.Add(new StartupActionRecord());
        await actions.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        try { Directory.Delete(_libDir, recursive: true); } catch { }
        await _postgres.DropDatabaseAsync(_dbName);
    }

    [Fact]
    public async Task Stats_ReturnsCountsAcrossTheSeriesNotificationsAndActionsDatabases()
    {
        using HttpClient http = _app.CreateClient();

        var stats = await http.GetFromJsonAsync<Stats>("/v2/Stats");

        Assert.NotNull(stats);
        Assert.Equal(2, stats!.NumberManga);
        Assert.Equal(2, stats.NumberChapters);
        Assert.Equal(1, stats.DownloadedChapters);
        Assert.Equal(1, stats.MissingChapters);
        Assert.Equal(1, stats.SentNotifications);
        Assert.Equal(1, stats.ActionsTaken);
    }
}
