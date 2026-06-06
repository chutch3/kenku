using System.Text.Json;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>Payload for <see cref="ReconcileVolumeBundleHandler"/>.</summary>
public record ReconcileVolumeBundlePayload(string SeriesKey, int Volume);

/// <summary>
/// Reconciles one VolumeCBZ volume's bundle (bundle / rebuild / no-op) via <see cref="VolumeBundler"/>.
/// Resolves its own scoped <see cref="SeriesContext"/> so each job runs against a fresh DbContext
/// (§4.1), and is therefore safe to register as a singleton handler.
/// </summary>
public class ReconcileVolumeBundleHandler(IServiceScopeFactory scopeFactory, KenkuSettings settings) : IJobHandler
{
    public const string Type = "ReconcileVolumeBundle";
    public string JobType => Type;

    public static string PayloadFor(string seriesKey, int volume) =>
        JsonSerializer.Serialize(new ReconcileVolumeBundlePayload(seriesKey, volume));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        ReconcileVolumeBundlePayload payload = JsonSerializer.Deserialize<ReconcileVolumeBundlePayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        SeriesContext context = scope.ServiceProvider.GetRequiredService<SeriesContext>();
        await new VolumeBundler(settings).ReconcileAsync(context, payload.SeriesKey, payload.Volume, ct);
    }
}
