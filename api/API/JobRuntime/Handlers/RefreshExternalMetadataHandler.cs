using System.Text.Json;
using API.Schema.ActionsContext;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Schema.SeriesContext.MetadataFetchers;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>Payload for <see cref="RefreshExternalMetadataHandler"/>.</summary>
public record RefreshExternalMetadataPayload(string MangaId);

/// <summary>
/// Refreshes one series' external metadata via <see cref="MetadataRefreshService"/> — replacing the bulk
/// UpdateMetadataWorker. Resolves its own scoped contexts (§4.1).
/// </summary>
public class RefreshExternalMetadataHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "RefreshExternalMetadata";
    public string JobType => Type;

    public static string PayloadFor(string mangaId) => JsonSerializer.Serialize(new RefreshExternalMetadataPayload(mangaId));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        RefreshExternalMetadataPayload payload = JsonSerializer.Deserialize<RefreshExternalMetadataPayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var service = new MetadataRefreshService(provider.GetServices<MetadataFetcher>());
        await service.RefreshAsync(
            provider.GetRequiredService<SeriesContext>(),
            provider.GetRequiredService<ActionsContext>(),
            payload.MangaId, ct);
    }
}
