using System.Net;
using API.HttpRequesters;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Server;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// AF2b wiring (the #31 download loop). Boots the real app so DI composes IHttpRequester over
/// RateLimitHandler exactly as in production, faking only the network edge. Proves the rate-limit queue
/// wait is not charged against the request timeout: a request merely waiting for a token survives the
/// timeout window and is stopped only by real (worker) cancellation.
/// </summary>
public class DownloadRateLimitWiringTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly KenkuApplicationFactory _app;

    public DownloadRateLimitWiringTests()
    {
        var inner = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        _app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            RateLimit = (inner, RequestsPerMinute: 1, QueueLimit: 10, RequestTimeout: TimeSpan.FromMilliseconds(300))
        };
    }

    public void Dispose()
    {
        _app.Dispose();
        _server.Stop();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RateLimitQueueWait_IsNotChargedAgainstTheRequestTimeout()
    {
        var requester = _app.Services.GetRequiredService<IHttpRequester>();

        HttpResponseMessage first = await requester.MakeRequest("http://img.test/1", RequestType.MangaImage);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // The second request to the same host has no token and must wait ~1 minute for replenishment. The
        // instant network plus the 300ms request timeout must NOT cancel it.
        using var cts = new CancellationTokenSource();
        Task<HttpResponseMessage> second = requester.MakeRequest("http://img.test/2", RequestType.MangaImage, cancellationToken: cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(750));
        Assert.False(second.IsCompleted);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }
}
