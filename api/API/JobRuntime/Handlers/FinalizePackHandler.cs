using API.DownloadClients.Interfaces;
using API.JobRuntime.Interfaces;
using System.Text.Json;
using API.Schema.ActionsContext;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>Payload for <see cref="FinalizePackHandler"/>: the pack's client tag, its series, and the save path.</summary>
public record FinalizePackPayload(string Tag, string SeriesKey, string SavePath);

/// <summary>
/// Finalises one completed pack torrent (tag <c>pack:{seriesKey}:{hash}</c>) by fanning its archives
/// out to every chapter of the series they cover — the pack counterpart of
/// <see cref="FinalizeTorrentHandler"/>.
/// </summary>
public class FinalizePackHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "FinalizePack";
    public string JobType => Type;

    public static string PayloadFor(string tag, string seriesKey, string savePath) =>
        JsonSerializer.Serialize(new FinalizePackPayload(tag, seriesKey, savePath));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        FinalizePackPayload payload = JsonSerializer.Deserialize<FinalizePackPayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        await provider.GetRequiredService<TorrentFinalizationService>().FinalizePackAsync(
            provider.GetRequiredService<SeriesContext>(),
            provider.GetRequiredService<ActionsContext>(),
            provider.GetRequiredService<IDownloadClient>(),
            provider.GetRequiredService<KenkuSettings>(),
            payload.Tag, payload.SeriesKey, payload.SavePath, ct);
    }
}
