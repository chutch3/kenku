using System.Net;
using System.Text;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace API.Tests.Integration;

/// <summary>
/// Base for outside-in tests that host the real app and stub only the HTTP edges. Owns the WireMock
/// server (the resolvers' outbound target) and the <see cref="KenkuApplicationFactory"/>, and provides the
/// shared scaffolding — polling, library seeding, and MangaDex/Wikipedia/connector stubs — so individual
/// tests express only what's specific to them.
/// </summary>
public abstract class OutboundHttpIntegrationTest : IDisposable
{
    protected readonly WireMockServer Server = WireMockServer.Start();
    protected readonly KenkuApplicationFactory App;

    protected OutboundHttpIntegrationTest(IHttpRequester? connectorHttp = null) =>
        App = new KenkuApplicationFactory { OutboundHttpTarget = Server.Url!, ConnectorHttpRequester = connectorHttp };

    public virtual void Dispose()
    {
        App.Dispose();
        Server.Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>Runs the dispatcher until the job queue is drained — the test-mode substitute for the
    /// hosted worker pool (disabled under RunStartup=false).</summary>
    protected async Task DrainJobsAsync(int maxJobs = 200)
    {
        for (int i = 0; i < maxJobs; i++)
        {
            using var scope = App.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<API.JobRuntime.Dispatcher>().RunOnceAsync())
                return;
        }
    }

    /// <summary>Polls <paramref name="condition"/> until it holds or the (short, fail-fast) timeout elapses.</summary>
    protected static async Task<bool> WaitUntil(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(50);
        }
        return false;
    }

    /// <summary>Seeds a writable <see cref="FileLibrary"/> and returns its key.</summary>
    protected Task<string> SeedLibrary() => App.WithSeriesContext(async ctx =>
    {
        var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-it-" + Guid.NewGuid().ToString("N")), "Lib");
        ctx.FileLibraries.Add(library);
        await ctx.SaveChangesAsync();
        return library.Key;
    });

    /// <summary>Builds an <see cref="IHttpRequester"/> stub that returns <paramref name="html"/> for any request.</summary>
    protected static IHttpRequester ConnectorReturning(string html)
    {
        var mock = new Mock<IHttpRequester>();
        mock.Setup(c => c.MakeRequest(It.IsAny<string>(), It.IsAny<RequestType>(), It.IsAny<string>(), It.IsAny<CancellationToken?>()))
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });
        return mock.Object;
    }

    protected void StubMangaDexSearch(string json) =>
        Server.Given(Request.Create().WithPath("/manga").WithParam("title").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(json));

    protected void StubMangaDexAggregate(string json) =>
        Server.Given(Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(json));

    protected void StubWikipediaEmpty() =>
        Server.Given(Request.Create().WithPath("/w/api.php").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(IntegrationFixtures.EmptyWikipedia));

    /// <summary>The single MetadataSource row for the given series.</summary>
    protected Task<MetadataSource> MetadataSourceFor(string mangaKey) =>
        App.WithSeriesContext(c => c.Set<MetadataSource>().FirstAsync(s => s.MangaId == mangaKey));
}
