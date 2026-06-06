using System.Text.Json;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>Payload for <see cref="ResolveSeriesVolumesHandler"/>.</summary>
public record ResolveSeriesVolumesPayload(string SeriesKey);

/// <summary>
/// Resolves one series' chapter→volume assignments via <see cref="VolumeResolutionService"/> (auto-match,
/// exact sources, colour heuristic) — replacing the ResolveMissingVolumesForManga worker. Resolves its own
/// scoped <see cref="SeriesContext"/> and resolver so each job runs against a fresh DbContext (§4.1).
/// </summary>
public class ResolveSeriesVolumesHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "ResolveSeriesVolumes";
    public string JobType => Type;

    public static string PayloadFor(string seriesKey) =>
        JsonSerializer.Serialize(new ResolveSeriesVolumesPayload(seriesKey));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        ResolveSeriesVolumesPayload payload = JsonSerializer.Deserialize<ResolveSeriesVolumesPayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        SeriesContext context = scope.ServiceProvider.GetRequiredService<SeriesContext>();
        VolumeResolutionService resolver = scope.ServiceProvider.GetRequiredService<VolumeResolutionService>();
        await resolver.ResolveAsync(context, payload.SeriesKey, ct);
    }
}
