using API.DownloadClients.Interfaces;
using API.JobRuntime.Interfaces;
using System.Text.Json;
using API.DownloadClients;
using API.Schema.ActionsContext;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>Payload for <see cref="FinalizeTorrentHandler"/>: the chapter source and the torrent's save path.</summary>
public record FinalizeTorrentPayload(string SourceIdKey, string SavePath);

/// <summary>
/// Finalises one completed torrent via <see cref="TorrentFinalizationService"/> — replacing the
/// per-completion half of TorrentCompletionWorker. Resolves its own scoped contexts (§4.1); the download
/// client is only ever resolved here, so non-torrent deployments (which never enqueue this job) are
/// unaffected by it being registered.
/// </summary>
public class FinalizeTorrentHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "FinalizeTorrent";
    public string JobType => Type;

    public static string PayloadFor(string sourceIdKey, string savePath) =>
        JsonSerializer.Serialize(new FinalizeTorrentPayload(sourceIdKey, savePath));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        FinalizeTorrentPayload payload = JsonSerializer.Deserialize<FinalizeTorrentPayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        await provider.GetRequiredService<TorrentFinalizationService>().FinalizeAsync(
            provider.GetRequiredService<SeriesContext>(),
            provider.GetRequiredService<ActionsContext>(),
            provider.GetRequiredService<IDownloadClient>(),
            provider.GetRequiredService<KenkuSettings>(),
            payload.SourceIdKey, payload.SavePath, ct);
    }
}
