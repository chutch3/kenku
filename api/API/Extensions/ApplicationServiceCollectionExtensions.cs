using API.Notifications.Interfaces;
using API.Services.Interfaces;
using API.Acquirers.Interfaces;
using API.JobRuntime.Interfaces;
using API.Acquirers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.JobRuntime.Reconcilers;
using API.Connectors;
using API.HttpRequesters;
using API.HttpRequesters.Interfaces;
using API.MetadataResolvers;
using API.MetadataResolvers.Interfaces;
using API.Notifications;
using API.Schema.ActionsContext;
using API.Schema.LibraryContext;
using API.Schema.NotificationsContext;
using API.Schema.SeriesContext;
using API.Schema.SeriesContext.MetadataFetchers;
using API.Schema.SeriesContext.MetadataFetchers.Interfaces;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace API.Extensions;

/// <summary>One layer per method, called from Program.cs — registration lives here, not inline.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>The five DbContexts against the shared Postgres connection.</summary>
    public static IServiceCollection AddKenkuDatabase(this IServiceCollection services)
    {
        NpgsqlConnectionStringBuilder connectionStringBuilder = new()
        {
            Host = Constants.PostgresHost,
            Database = Constants.PostgresDb,
            Username = Constants.PostgresUser,
            Password = Constants.PostgresPassword,
            ConnectionLifetime = 300,
            Timeout = Constants.PostgresConnectionTimeout,
            ReadBufferSize = 65536,
            WriteBufferSize = 65536,
            CommandTimeout = Constants.PostgresCommandTimeout,
            ApplicationName = "Kenku"
        };

        services.AddDbContext<SeriesContext>(options =>
            options.UseNpgsql(connectionStringBuilder.ConnectionString, SeriesContextOptions.Configure));
        services.AddDbContext<NotificationsContext>(options =>
            options.UseNpgsql(connectionStringBuilder.ConnectionString));
        services.AddDbContext<LibraryContext>(options =>
            options.UseNpgsql(connectionStringBuilder.ConnectionString));
        services.AddDbContext<ActionsContext>(options =>
            options.UseNpgsql(connectionStringBuilder.ConnectionString));
        services.AddDbContext<Schema.JobsContext.JobsContext>(options =>
            options.UseNpgsql(connectionStringBuilder.ConnectionString));
        return services;
    }

    /// <summary>The scrape-site connectors. Registered as the base SeriesSource so DI groups them;
    /// the indexer-backed source registers separately in AddTorrentAcquisitionPath.</summary>
    public static IServiceCollection AddKenkuConnectors(this IServiceCollection services)
    {
        services.AddSingleton<SeriesSource, Global>();
        services.AddSingleton<SeriesSource, AsuraComic>();
        services.AddSingleton<SeriesSource, ComicHubFree>();
        services.AddSingleton<SeriesSource, GetComics>();
        services.AddSingleton<SeriesSource, MangaDex>();
        services.AddSingleton<SeriesSource, Mangaworld>();
        services.AddSingleton<SeriesSource, WeebCentral>();
        return services;
    }

    public static IServiceCollection AddMetadataFetchers(this IServiceCollection services, KenkuSettings settings)
    {
        services.AddSingleton<JikanDotNet.IJikan>(new JikanDotNet.Jikan());
        services.AddSingleton<MetadataFetcher, MyAnimeList>();
        // Metron comic metadata: client reads creds from settings; the fetcher always appears in the list
        // and degrades gracefully (returns nothing) when unconfigured.
        services.AddSingleton<IMetronClient>(sp =>
        {
            var rl = sp.GetRequiredService<RateLimitHandler>();
            return new MetronClient(new HttpClient(rl, disposeHandler: false), settings);
        });
        services.AddSingleton<MetadataFetcher>(sp => new Metron(sp.GetRequiredService<IMetronClient>()));
        return services;
    }

    /// <summary>One chapter acquirer per AcquisitionKind (the torrent acquirer is gated, in AddTorrentAcquisitionPath).</summary>
    public static IServiceCollection AddChapterAcquirers(this IServiceCollection services)
    {
        services.AddSingleton<IChapterAcquirer, ImageListAcquirer>();
        services.AddSingleton<IChapterAcquirer>(sp =>
        {
            // Reuse the shared RateLimitHandler so direct-archive downloads honour per-host politeness too.
            var rl = sp.GetRequiredService<RateLimitHandler>();
            return new DirectArchiveAcquirer(new HttpClient(rl, disposeHandler: false));
        });
        return services;
    }

    /// <summary>Volume/search metadata resolvers, each on its own typed HttpClient.</summary>
    public static IServiceCollection AddMetadataResolvers(this IServiceCollection services)
    {
        services.AddHttpClient<MangaDexVolumeResolver>();
        services.AddSingleton<IMangaDexVolumeResolver>(sp => sp.GetRequiredService<MangaDexVolumeResolver>());
        services.AddHttpClient<WikipediaVolumeResolver>();
        services.AddSingleton<IVolumeResolver>(sp => sp.GetRequiredService<WikipediaVolumeResolver>());
        services.AddHttpClient<MangaDexSearchService>();
        services.AddSingleton<IMangaDexSearchService>(sp => sp.GetRequiredService<MangaDexSearchService>());
        services.AddHttpClient<AniListSearchService>();
        services.AddSingleton<IAniListSearchService>(sp => sp.GetRequiredService<AniListSearchService>());
        return services;
    }

    /// <summary>
    /// The domain services job handlers and controllers resolve. Scoped, so a service created inside a
    /// job's scope shares that scope's DbContexts and job store (§4.1 — one fresh context per job).
    /// </summary>
    public static IServiceCollection AddKenkuServices(this IServiceCollection services)
    {
        services.AddSingleton<INotificationDispatcher, DbNotificationDispatcher>();
        services.AddSingleton<IChapterThumbnailService, ChapterThumbnailService>();
        services.AddSingleton<ILibraryLayoutResolver, LibraryLayoutResolver>();
        services.AddScoped<ChapterDownloadService>();
        services.AddScoped<ChapterFilePlacementService>();
        services.AddScoped<CleanupService>();
        services.AddScoped<CoverDownloadService>();
        services.AddScoped<DataMoveService>();
        services.AddScoped<DownloadStateService>();
        services.AddScoped<MetadataRefreshService>();
        services.AddScoped<SeriesChapterSyncService>();
        services.AddScoped<SeriesLibraryService>();
        services.AddScoped<SeriesRollupService>();
        services.AddScoped<TorrentFinalizationService>();
        services.AddScoped<VolumeBundler>();
        services.AddScoped<VolumeResolutionService>();
        services.AddSingleton<Kenku>();
        return services;
    }

    /// <summary>The job runtime: clock, registries, every handler, the EF store, dispatcher and worker pool.</summary>
    public static IServiceCollection AddJobRuntime(this IServiceCollection services, KenkuSettings settings)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(sp => new HandlerRegistry(sp.GetServices<IJobHandler>()));
        services.AddSingleton<RunningJobRegistry>();
        services.AddSingleton<IJobHandler, ReconcileVolumeBundleHandler>();
        services.AddSingleton<IJobHandler, ResolveSeriesVolumesHandler>();
        services.AddSingleton<IJobHandler, DownloadChapterHandler>();
        services.AddSingleton<IJobHandler, RefreshLibrariesHandler>();
        services.AddSingleton<IJobHandler, SyncSeriesChaptersHandler>();
        services.AddSingleton<IJobHandler, CleanupHandler>();
        services.AddSingleton<IJobHandler, RefreshExternalMetadataHandler>();
        services.AddSingleton<IJobHandler, SendNotificationsHandler>();
        services.AddSingleton<IJobHandler, PlaceChapterFileHandler>();
        services.AddSingleton<IJobHandler, DownloadCoverHandler>();
        services.AddSingleton<IJobHandler, FinalizeTorrentHandler>();
        services.AddSingleton<IJobHandler, VerifyDownloadStateHandler>();
        services.AddSingleton<IJobHandler, MoveDataHandler>();
        services.AddScoped<IJobStore, EfJobStore>();
        // Overall download concurrency is bounded by MaxConcurrentDownloads (per-host rate limiting is
        // separate, in RateLimitHandler); per-series fairness comes from the dispatcher's per-resource cap.
        services.AddScoped(sp => new Dispatcher(
            sp.GetRequiredService<IJobStore>(),
            sp.GetRequiredService<HandlerRegistry>(),
            sp.GetRequiredService<IClock>(),
            globalCap: Math.Max(1, settings.MaxConcurrentDownloads),
            running: sp.GetRequiredService<RunningJobRegistry>()));
        services.AddHostedService<JobPoolService>();
        return services;
    }

    /// <summary>The hosted reconcilers — the level-triggered backstops that turn drift into jobs.</summary>
    public static IServiceCollection AddReconcilers(this IServiceCollection services)
    {
        services.AddHostedService<VolumeBundleReconciler>();
        services.AddHostedService<VolumeResolutionReconciler>();
        services.AddHostedService<DownloadReconciler>();
        services.AddHostedService<SeriesChapterSyncReconciler>();
        services.AddHostedService<CleanupReconciler>();
        services.AddHostedService<MetadataRefreshReconciler>();
        services.AddHostedService<NotificationReconciler>();
        services.AddHostedService<ChapterFilePlacementReconciler>();
        services.AddHostedService<CoverRefreshReconciler>();
        services.AddHostedService<DownloadStateReconciler>();
        return services;
    }
}
