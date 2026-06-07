using System.Text.Json;
using API;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.ActionsContext;
using API.Schema.JobsContext;
using API.Schema.NotificationsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.Tests.Integration;

/// <summary>
/// Runs real reconcile→job flows end-to-end against shared in-memory databases, seeding only what a user
/// would provide and asserting the real outcome (files on disk, DB state). Each job runs in a FRESH
/// DI scope/context — so navigations come from the job's own query (its Includes), not in-memory
/// references — which is what isolated unit tests miss. See issue #19.
/// </summary>
public sealed class IntegrationHarness : IDisposable
{
    public string TempDir { get; }
    public KenkuSettings Settings { get; }

    private readonly ServiceProvider _root;
    private readonly string _seriesDb = "series-" + Guid.NewGuid();
    private readonly string _actionsDb = "actions-" + Guid.NewGuid();
    private readonly string _notifDb = "notif-" + Guid.NewGuid();

    public IntegrationHarness(string? chapterNamingScheme = null)
    {
        TempDir = Path.Combine(Path.GetTempPath(), "integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDir);
        Settings = new KenkuSettings { AppData = TempDir };
        if (chapterNamingScheme is not null)
            Settings.ChapterNamingScheme = chapterNamingScheme;

        var services = new ServiceCollection();
        services.AddDbContext<SeriesContext>(o => o.UseInMemoryDatabase(_seriesDb));
        services.AddDbContext<ActionsContext>(o => o.UseInMemoryDatabase(_actionsDb));
        services.AddDbContext<NotificationsContext>(o => o.UseInMemoryDatabase(_notifDb));
        _root = services.BuildServiceProvider();
    }

    /// <summary>A fresh DI scope with its own context instances (sharing the same in-memory store).</summary>
    public IServiceScope CreateScope() => _root.CreateScope();

    /// <summary>The root provider, for components that create their own scopes.</summary>
    public IServiceProvider Services => _root;

    /// <summary>Run an action against a fresh SeriesContext and persist it (seeding user input).</summary>
    public async Task Seed(Func<SeriesContext, Task> seed)
    {
        using var scope = _root.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SeriesContext>();
        await seed(ctx);
        await ctx.SaveChangesAsync();
    }

    /// <summary>Read assertion against a fresh SeriesContext.</summary>
    public async Task<T> Query<T>(Func<SeriesContext, Task<T>> query)
    {
        using var scope = _root.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<SeriesContext>());
    }

    /// <summary>
    /// Reconcile chapter file placement the way the runtime does: scan for drifted chapters, enqueue
    /// PlaceChapterFile jobs, then run each job's placement logic in its own fresh scope (#19). Returns
    /// the jobs enqueued.
    /// </summary>
    public async Task<IReadOnlyList<Job>> ReconcileChapterFilePlacement()
    {
        var store = new InMemoryJobStore();
        using (var scope = CreateScope())
            await ChapterFilePlacementReconciler.ScanAndEnqueueAsync(
                scope.ServiceProvider.GetRequiredService<SeriesContext>(), store, Settings, DateTime.UtcNow, default);

        IReadOnlyList<Job> jobs = await store.GetAllAsync();
        foreach (Job job in jobs)
        {
            PlaceChapterFilePayload payload = JsonSerializer.Deserialize<PlaceChapterFilePayload>(job.Payload)!;
            using var scope = CreateScope();
            await new ChapterFilePlacementService(Settings).PlaceAsync(
                scope.ServiceProvider.GetRequiredService<SeriesContext>(), payload.ChapterKey, payload.TargetFileName, default);
        }
        return jobs;
    }

    public void Dispose()
    {
        _root.Dispose();
        try { Directory.Delete(TempDir, recursive: true); } catch { /* best effort */ }
    }
}
