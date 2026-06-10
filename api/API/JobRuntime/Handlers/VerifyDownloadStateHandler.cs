using API.JobRuntime.Interfaces;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Handlers;

/// <summary>
/// Re-checks every chapter's Downloaded flag against disk via <see cref="DownloadStateService"/> —
/// replacing the UpdateChaptersDownloaded worker. A single payload-less bulk job; resolves its own scoped
/// <see cref="SeriesContext"/> (§4.1).
/// </summary>
public class VerifyDownloadStateHandler(IServiceScopeFactory scopeFactory) : IJobHandler
{
    public const string Type = "VerifyDownloadState";
    public string JobType => Type;

    public static string Payload() => "{}";

    public async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        await provider.GetRequiredService<DownloadStateService>().VerifyAllAsync(
            provider.GetRequiredService<SeriesContext>(), provider.GetRequiredService<KenkuSettings>(), ct);
    }
}
