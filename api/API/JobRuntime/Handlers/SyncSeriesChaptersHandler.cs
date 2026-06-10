using API.JobRuntime.Interfaces;
using System.Text.Json;
using API.Schema.ActionsContext;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>Payload for <see cref="SyncSeriesChaptersHandler"/>: the series' connector source-id + language.</summary>
public record SyncSeriesChaptersPayload(string SourceIdKey, string Language);

/// <summary>
/// Syncs one series' chapter list from its connector via <see cref="SeriesChapterSyncService"/> — replacing
/// the RetrieveChaptersFromSource worker. Resolves its own scoped contexts (§4.1).
/// </summary>
public class SyncSeriesChaptersHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "SyncSeriesChapters";
    public string JobType => Type;

    public static string PayloadFor(string sourceIdKey, string language) =>
        JsonSerializer.Serialize(new SyncSeriesChaptersPayload(sourceIdKey, language));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        SyncSeriesChaptersPayload payload = JsonSerializer.Deserialize<SyncSeriesChaptersPayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var service = provider.GetRequiredService<SeriesChapterSyncService>();
        (int reported, int added) = await service.SyncAsync(
            provider.GetRequiredService<SeriesContext>(),
            provider.GetRequiredService<ActionsContext>(),
            payload.SourceIdKey, payload.Language, ct);
        job.Progress = $"connector reported {reported} chapters ({added} new)";
    }
}
