using API.Acquirers.Interfaces;
using API.JobRuntime.Interfaces;
using System.Text.Json;
using API.Acquirers;
using API.MangaConnectors;
using API.Schema.ActionsContext;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>Payload for <see cref="DownloadChapterHandler"/>.</summary>
public record DownloadChapterPayload(string ChapterKey);

/// <summary>
/// Downloads one chapter via <see cref="ChapterDownloadService"/> (resolve → acquire .cbz → mark
/// Downloaded → cover → enqueue ready bundles) — replacing the DownloadChapterFromSource worker. Resolves
/// its own scoped contexts so each job runs against fresh DbContexts (§4.1). A failure throws so the
/// dispatcher records it and applies bounded retry / NeedsAttention (AF2c).
/// </summary>
public class DownloadChapterHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "DownloadChapter";
    public string JobType => Type;

    public static string PayloadFor(string chapterKey) =>
        JsonSerializer.Serialize(new DownloadChapterPayload(chapterKey));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        DownloadChapterPayload payload = JsonSerializer.Deserialize<DownloadChapterPayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var service = new ChapterDownloadService(
            provider.GetRequiredService<KenkuSettings>(),
            provider.GetServices<SeriesSource>(),
            provider.GetRequiredService<IJobStore>(),
            provider.GetRequiredService<IClock>(),
            provider.GetServices<IChapterAcquirer>(),
            new LibraryLayoutResolver(),
            provider.GetRequiredService<API.Notifications.Interfaces.INotificationDispatcher>());

        await service.DownloadAsync(
            provider.GetRequiredService<SeriesContext>(),
            provider.GetRequiredService<ActionsContext>(),
            payload.ChapterKey, ct);
    }
}
