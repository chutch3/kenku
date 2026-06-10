using API.Notifications.Interfaces;
using API.Services.Interfaces;
using API.Services;

namespace API.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// The domain services job handlers and controllers resolve. Scoped, so a service created inside a
    /// job's scope shares that scope's DbContexts and job store (§4.1 — one fresh context per job).
    /// </summary>
    public static IServiceCollection AddKenkuServices(this IServiceCollection services)
    {
        services.AddSingleton<ILibraryLayoutResolver, LibraryLayoutResolver>();
        services.AddScoped<ChapterDownloadService>();
        services.AddScoped<ChapterFilePlacementService>();
        services.AddScoped<CleanupService>();
        services.AddScoped<CoverDownloadService>();
        services.AddScoped<DataMoveService>();
        services.AddScoped<DownloadStateService>();
        services.AddScoped<MetadataRefreshService>();
        services.AddScoped<SeriesChapterSyncService>();
        services.AddScoped<TorrentFinalizationService>();
        services.AddScoped<VolumeBundler>();
        services.AddScoped<VolumeResolutionService>();
        return services;
    }
}
