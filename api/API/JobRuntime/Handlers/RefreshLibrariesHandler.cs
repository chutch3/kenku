using API.JobRuntime.Interfaces;
using API.Schema.JobsContext;
using API.Schema.LibraryContext;
using API.Schema.LibraryContext.LibraryConnectors;
using log4net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>
/// Triggers a rescan on every configured external library connector (Kavita/Komga/...). Replaces the
/// RefreshLibrariesWorker; enqueued by <see cref="API.Services.ChapterDownloadService"/> after a download
/// per the user's <see cref="API.Workers.LibraryRefreshSetting"/>.
/// </summary>
public class RefreshLibrariesHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(RefreshLibrariesHandler));

    public const string Type = "RefreshLibraries";
    public string JobType => Type;

    /// <summary>When libraries were last refreshed — drives the WhileDownloading debounce.</summary>
    public static DateTime LastRefresh { get; set; } = DateTime.UnixEpoch;

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        Log.Debug("Refreshing libraries...");
        LastRefresh = DateTime.UtcNow;
        using IServiceScope scope = scopeFactory.CreateScope();
        LibraryContext context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
        foreach (LibraryConnector connector in await context.LibraryConnectors.ToListAsync(ct))
            await connector.UpdateLibrary(ct);
        Log.Debug("Libraries Refreshed...");
    }
}
