using API.JobRuntime.Interfaces;
using System.Text.Json;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>What a <see cref="ReconcileVolumeBundleHandler"/> job should do to the volume.</summary>
public enum BundleAction
{
    /// <summary>Make the bundle correct: bundle if ready, rebuild if stale, no-op if fresh (reconciler/auto).</summary>
    Reconcile,
    /// <summary>Force-bundle the volume's loose chapters now (manual bundle).</summary>
    Bundle,
    /// <summary>Split the bundle back into chapter files (manual unbundle).</summary>
    Unbundle
}

/// <summary>Payload for <see cref="ReconcileVolumeBundleHandler"/>.</summary>
public record ReconcileVolumeBundlePayload(string SeriesKey, int Volume, BundleAction Action = BundleAction.Reconcile);

/// <summary>
/// The single home for VolumeCBZ bundling: reconcile (bundle / rebuild / no-op), force-bundle, or unbundle
/// one volume via <see cref="VolumeBundler"/> — replacing the BundleVolume / UnbundleVolume /
/// EnsureReadyVolumesBundled / EnsureBundledVolumesFresh workers. Resolves its own scoped
/// <see cref="SeriesContext"/> so each job runs against a fresh DbContext (§4.1), and is therefore safe to
/// register as a singleton handler.
/// </summary>
public class ReconcileVolumeBundleHandler(IServiceScopeFactory scopeFactory, KenkuSettings settings) : IJobHandler
{
    public const string Type = "ReconcileVolumeBundle";
    public string JobType => Type;

    public static string PayloadFor(string seriesKey, int volume, BundleAction action = BundleAction.Reconcile) =>
        JsonSerializer.Serialize(new ReconcileVolumeBundlePayload(seriesKey, volume, action));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        ReconcileVolumeBundlePayload payload = JsonSerializer.Deserialize<ReconcileVolumeBundlePayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        SeriesContext context = scope.ServiceProvider.GetRequiredService<SeriesContext>();
        var bundler = new VolumeBundler(settings);

        Task work = payload.Action switch
        {
            BundleAction.Bundle => bundler.BundleAsync(context, payload.SeriesKey, payload.Volume, ct),
            BundleAction.Unbundle => bundler.UnbundleAsync(context, payload.SeriesKey, payload.Volume, ct),
            _ => bundler.ReconcileAsync(context, payload.SeriesKey, payload.Volume, ct)
        };
        await work;
    }
}
