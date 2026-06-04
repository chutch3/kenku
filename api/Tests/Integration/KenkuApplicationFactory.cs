using API.HttpRequesters;
using API.Schema.ActionsContext;
using API.Schema.LibraryContext;
using API.Schema.NotificationsContext;
using API.Schema.SeriesContext;
using API.Services;
using API.Workers.MaintenanceWorkers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public required string OutboundHttpTarget { get; init; }

    /// <summary>Optional stub for the connectors' HTTP edge, so a connector flow can be driven without
    /// real network access. When set, it replaces the registered <see cref="IHttpRequester"/>.</summary>
    public IHttpRequester? ConnectorHttpRequester { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Kenku:RunStartup", "false");
        // Isolated, writable settings location — never touches the real path or the global APP_DATA env.
        builder.UseSetting("Kenku:AppData", Path.Combine(Path.GetTempPath(), "kenku-test-" + _id));
        builder.ConfigureTestServices(services =>
        {
            UseInMemory<SeriesContext>(services);
            UseInMemory<NotificationsContext>(services);
            UseInMemory<LibraryContext>(services);
            UseInMemory<ActionsContext>(services);

            RouteOutboundHttp<MangaDexVolumeResolver>(services);
            RouteOutboundHttp<MangaDexSearchService>(services);
            RouteOutboundHttp<WikipediaVolumeResolver>(services);

            if (ConnectorHttpRequester is not null)
            {
                services.RemoveAll<IHttpRequester>();
                services.AddSingleton(ConnectorHttpRequester);
            }
        });
    }

    private void UseInMemory<TContext>(IServiceCollection services) where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<TContext>();
        services.AddDbContext<TContext>(o => o
            .UseInMemoryDatabase($"{typeof(TContext).Name}-{_id}")
            .UseInternalServiceProvider(_efProvider));
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
}
