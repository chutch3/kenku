using API.JobRuntime.Interfaces;
using System.Text.Json;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>
/// Payload for <see cref="PlaceChapterFileHandler"/>. A null <see cref="TargetFileName"/> means "compute
/// the layout-correct name" (the reconciliation path); an explicit name is used verbatim (the reorganize
/// path).
/// </summary>
public record PlaceChapterFilePayload(string ChapterKey, string? TargetFileName = null);

/// <summary>
/// Moves one chapter's archive to its layout-correct path via <see cref="ChapterFilePlacementService"/> —
/// replacing the RenameChapterFile worker. Resolves its own scoped <see cref="SeriesContext"/> (§4.1).
/// </summary>
public class PlaceChapterFileHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "PlaceChapterFile";
    public string JobType => Type;

    public static string PayloadFor(string chapterKey, string? targetFileName = null) =>
        JsonSerializer.Serialize(new PlaceChapterFilePayload(chapterKey, targetFileName));

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        PlaceChapterFilePayload payload = JsonSerializer.Deserialize<PlaceChapterFilePayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var service = provider.GetRequiredService<ChapterFilePlacementService>();
        await service.PlaceAsync(provider.GetRequiredService<SeriesContext>(), payload.ChapterKey, payload.TargetFileName, ct);
    }
}
