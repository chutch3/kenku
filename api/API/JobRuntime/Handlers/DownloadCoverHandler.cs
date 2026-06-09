using API.JobRuntime.Interfaces;
using System.Text.Json;
using API.Connectors;
using API.Schema.ActionsContext;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>Payload for <see cref="DownloadCoverHandler"/>: the series source whose cover to cache.</summary>
public record DownloadCoverPayload(string SourceIdKey);

/// <summary>
/// Caches one series source's cover via <see cref="CoverDownloadService"/> — replacing the
/// DownloadCoverFromSource worker. Resolves its own scoped contexts (§4.1).
/// </summary>
public class DownloadCoverHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "DownloadCover";
    public string JobType => Type;

    public static string PayloadFor(string sourceIdKey) =>
        JsonSerializer.Serialize(new DownloadCoverPayload(sourceIdKey));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        DownloadCoverPayload payload = JsonSerializer.Deserialize<DownloadCoverPayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var service = new CoverDownloadService(provider.GetServices<SeriesSource>());
        CoverOutcome outcome = await service.DownloadAsync(provider.GetRequiredService<SeriesContext>(),
            provider.GetRequiredService<ActionsContext>(), payload.SourceIdKey, ct);
        job.Progress = outcome switch
        {
            CoverOutcome.Cached => "cover cached",
            CoverOutcome.NoCoverUrl => "this source provides no cover URL — link MyAnimeList or Metron to backfill one",
            CoverOutcome.SourceMissing => "the source link no longer exists",
            CoverOutcome.FetchFailed => "the cover could not be fetched from its URL",
            _ => job.Progress
        };
    }
}
