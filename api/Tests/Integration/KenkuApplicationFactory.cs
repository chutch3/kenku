using API.HttpRequesters.Interfaces;
using API.HttpRequesters;
using API.Schema.ActionsContext;
using API.Schema.LibraryContext;
using API.Schema.NotificationsContext;
using API.Schema.SeriesContext;
using API.Services;
using API.MetadataResolvers;
using API.MetadataResolvers.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace API.Tests.Integration;

/// <summary>
/// Hosts the REAL application in-process for integration tests. The DI container builds everything
/// (controllers, workers, resolvers, HttpClients) exactly as in production; the test only swaps the two
/// edges: the databases become in-memory, and the metadata HttpClients are pointed at a local server
/// (WireMock) via <paramref name="OutboundHttpTarget"/>. Startup workers/migrations are disabled
/// (Kenku:RunStartup=false) so hosting the app doesn't auto-run workers or hit the real network.
/// </summary>
public sealed class KenkuApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _id = Guid.NewGuid().ToString("N");

    // A dedicated EF service provider for the in-memory contexts, so they don't clash with the
    // production Npgsql provider that's also registered in the app container.
    private readonly IServiceProvider _efProvider =
        new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();

    /// <summary>Base URL of the local server outbound metadata requests should be redirected to.</summary>
    public string OutboundHttpTarget { get; init; } = "http://localhost:1";

    /// <summary>When set, the three core contexts (Series, Jobs, Actions) are backed by this Postgres
    /// database instead of the default InMemory stores. Used by concurrency and migration tests that
    /// need a real relational engine.</summary>
    public string? PostgresConnectionString { get; init; }

    /// <summary>Optional stub for the connectors' HTTP edge, so a connector flow can be driven without
    /// real network access. When set, it replaces the registered <see cref="IHttpRequester"/>.</summary>
    public IHttpRequester? ConnectorHttpRequester { get; init; }

    /// <summary>Optional fake network edge for the rate-limited HTTP stack — the layer beneath
    /// <see cref="IHttpRequester"/>. When set, replaces the <see cref="RateLimitHandler"/> singleton with
    /// one built over this inner handler, so the real HttpRequester + RateLimitHandler composition is
    /// exercised without real network.</summary>
    public (HttpMessageHandler Inner, int RequestsPerMinute, int QueueLimit, TimeSpan RequestTimeout)? RateLimit { get; init; }

    /// <summary>Extra job handlers to register, so a test can enqueue and run a job through the real runtime
    /// without depending on a production handler existing yet.</summary>
    public IReadOnlyList<API.JobRuntime.Interfaces.IJobHandler> ExtraJobHandlers { get; init; } = [];

    /// <summary>Extra connectors to register, so a download/sync job can be driven against a stubbed
    /// <see cref="API.Connectors.SeriesSource"/> without real network.</summary>
    public IReadOnlyList<API.Connectors.SeriesSource> ExtraConnectors { get; init; } = [];

    /// <summary>Optional clock override so a test can control backoff/lease/scheduling windows without real
    /// waits — the time "edge", swapped like the DB and network edges. When set, replaces the singleton
    /// <see cref="API.JobRuntime.Interfaces.IClock"/> the dispatcher resolves.</summary>
    public API.JobRuntime.Interfaces.IClock? Clock { get; init; }

    /// <summary>Optional dispatcher concurrency caps, so fairness/concurrency behaviour is deterministic
    /// rather than derived from the host's CPU count. When set, the production-registered
    /// <see cref="API.JobRuntime.Dispatcher"/> is replaced with one using these caps (same store/registry/clock).</summary>
    public (int GlobalCap, int PerResourceCap)? DispatcherCaps { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Kenku:RunStartup", "false");
        // Isolated, writable settings location — never touches the real path or the global APP_DATA env.
        builder.UseSetting("Kenku:AppData", Path.Combine(Path.GetTempPath(), "kenku-test-" + _id));
        builder.ConfigureTestServices(services =>
        {
            if (PostgresConnectionString is not null)
            {
                UseNpgsql<SeriesContext>(services, PostgresConnectionString);
                UseNpgsql<global::API.Schema.JobsContext.JobsContext>(services, PostgresConnectionString);
                UseNpgsql<ActionsContext>(services, PostgresConnectionString);
                UseInMemory<NotificationsContext>(services);
                UseInMemory<LibraryContext>(services);
            }
            else
            {
                UseInMemory<SeriesContext>(services);
                UseInMemory<NotificationsContext>(services);
                UseInMemory<LibraryContext>(services);
                UseInMemory<ActionsContext>(services);
                UseInMemory<global::API.Schema.JobsContext.JobsContext>(services);
            }

            RouteOutboundHttp<MangaDexVolumeResolver>(services);
            RouteOutboundHttp<MangaDexSearchService>(services);
            RouteOutboundHttp<WikipediaVolumeResolver>(services);

            if (ConnectorHttpRequester is not null)
            {
                services.RemoveAll<IHttpRequester>();
                services.AddSingleton(ConnectorHttpRequester);
            }

            if (RateLimit is { } rl)
            {
                services.RemoveAll<RateLimitHandler>();
                services.AddSingleton(sp => new RateLimitHandler(
                    sp.GetRequiredService<KenkuSettings>(), rl.Inner, rl.RequestsPerMinute, rl.QueueLimit, rl.RequestTimeout));
            }

            if (Clock is not null)
            {
                services.RemoveAll<API.JobRuntime.Interfaces.IClock>();
                services.AddSingleton(Clock);
            }

            if (DispatcherCaps is { } caps)
            {
                services.RemoveAll<API.JobRuntime.Dispatcher>();
                services.AddScoped(sp => new API.JobRuntime.Dispatcher(
                    sp.GetRequiredService<API.JobRuntime.Interfaces.IJobStore>(),
                    sp.GetRequiredService<API.JobRuntime.HandlerRegistry>(),
                    sp.GetRequiredService<API.JobRuntime.Interfaces.IClock>(),
                    globalCap: caps.GlobalCap, perResourceCap: caps.PerResourceCap,
                    running: sp.GetRequiredService<API.JobRuntime.RunningJobRegistry>()));
            }

            foreach (API.JobRuntime.Interfaces.IJobHandler handler in ExtraJobHandlers)
                services.AddSingleton(handler);

            foreach (API.Connectors.SeriesSource connector in ExtraConnectors)
                services.AddSingleton(connector);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        if (PostgresConnectionString is not null)
        {
            using var scope = host.Services.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<SeriesContext>().Database.MigrateAsync().GetAwaiter().GetResult();
            sp.GetRequiredService<global::API.Schema.JobsContext.JobsContext>().Database.MigrateAsync().GetAwaiter().GetResult();
            sp.GetRequiredService<ActionsContext>().Database.MigrateAsync().GetAwaiter().GetResult();
        }
        return host;
    }

    private void UseInMemory<TContext>(IServiceCollection services) where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<TContext>();
        services.AddDbContext<TContext>(o => o
            .UseInMemoryDatabase($"{typeof(TContext).Name}-{_id}")
            .UseInternalServiceProvider(_efProvider));
    }

    private static void UseNpgsql<TContext>(IServiceCollection services, string connectionString) where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<TContext>();
        services.AddDbContext<TContext>(o => o.UseNpgsql(connectionString));
    }

    // Replaces the typed client's primary handler so its (absolute) requests are redirected to the
    // local server. The resolver's own configuration (e.g. the User-Agent) is left intact.
    private void RouteOutboundHttp<TClient>(IServiceCollection services) where TClient : class =>
        services.AddHttpClient<TClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HostRewritingHandler(OutboundHttpTarget));

    /// <summary>Runs <paramref name="action"/> against a fresh scope's SeriesContext (seed or assert).</summary>
    public async Task<T> WithSeriesContext<T>(Func<SeriesContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<SeriesContext>());
    }

    /// <summary>Runs <paramref name="action"/> against a fresh scope's JobsContext (assert persisted jobs).</summary>
    public async Task<T> WithJobsContext<T>(Func<global::API.Schema.JobsContext.JobsContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<global::API.Schema.JobsContext.JobsContext>());
    }
}
