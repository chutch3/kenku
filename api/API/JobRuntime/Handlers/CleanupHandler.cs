using API.JobRuntime.Interfaces;
using System.Text.Json;
using API.Connectors;
using API.Schema.JobsContext;
using API.Schema.NotificationsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>Payload for <see cref="CleanupHandler"/>. DryRun/Force apply only to <see cref="CleanupKind.OrphanedFiles"/>.</summary>
public record CleanupPayload(CleanupKind Kind, bool DryRun = false, bool Force = false);

/// <summary>
/// Parameterized cleanup job: runs one <see cref="CleanupKind"/> via <see cref="CleanupService"/> — replacing
/// the RemoveOldNotifications / CleanupMangaCovers / CleanupSourceIdsWithoutSource workers. Resolves its own
/// scoped contexts (§4.1).
/// </summary>
public class CleanupHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "Cleanup";
    public string JobType => Type;

    public static string PayloadFor(CleanupKind kind, bool dryRun = false, bool force = false) =>
        JsonSerializer.Serialize(new CleanupPayload(kind, dryRun, force));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        CleanupPayload payload = JsonSerializer.Deserialize<CleanupPayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var service = new CleanupService();

        switch (payload.Kind)
        {
            case CleanupKind.OldNotifications:
                await service.RemoveOldNotificationsAsync(provider.GetRequiredService<NotificationsContext>(), ct);
                break;
            case CleanupKind.MangaCovers:
                service.CleanupMangaCovers(provider.GetRequiredService<SeriesContext>(), provider.GetRequiredService<KenkuSettings>(), ct);
                break;
            case CleanupKind.OrphanSourceIds:
                await service.CleanupOrphanSourceIdsAsync(provider.GetRequiredService<SeriesContext>(),
                    provider.GetServices<SeriesSource>(), provider.GetRequiredService<KenkuSettings>(), ct);
                break;
            case CleanupKind.OrphanedFiles:
                await service.CleanupOrphanedFilesAsync(provider.GetRequiredService<SeriesContext>(),
                    payload.DryRun, payload.Force, ct);
                break;
        }
    }
}
