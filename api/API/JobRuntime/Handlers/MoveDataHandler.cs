using System.Text.Json;
using API.Schema.ActionsContext;
using API.Schema.JobsContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>Payload for <see cref="MoveDataHandler"/>: the source and destination paths.</summary>
public record MoveDataPayload(string From, string To);

/// <summary>
/// Moves one file or folder on disk via <see cref="DataMoveService"/> — replacing the MoveFileOrFolder
/// worker (chapter renames, series-library moves, merges). Resolves its own scoped <see cref="ActionsContext"/>.
/// </summary>
public class MoveDataHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "MoveData";
    public string JobType => Type;

    public static string PayloadFor(string from, string to) =>
        JsonSerializer.Serialize(new MoveDataPayload(from, to));

    public static string DedupKey(string to) => $"move:{to}";

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        MoveDataPayload payload = JsonSerializer.Deserialize<MoveDataPayload>(job.Payload)
            ?? throw new InvalidOperationException($"Invalid {Type} payload: {job.Payload}");

        using IServiceScope scope = scopeFactory.CreateScope();
        await new DataMoveService().MoveAsync(
            scope.ServiceProvider.GetRequiredService<ActionsContext>(), payload.From, payload.To, ct);
    }
}
