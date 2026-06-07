using System.Net;
using API;
using API.HttpRequesters;
using Xunit;

namespace API.Tests.Unit.HttpRequesters;

public class RateLimitHandlerTests
{
    [Fact]
    public async Task SendAsync_RateLimitsPerHost_BucketsAreIndependent()
    {
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };
        var inner = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        // One token per host, no queue: the second request to a host is immediately throttled.
        using var handler = new RateLimitHandler(settings, inner, requestsPerMinute: 1, queueLimit: 0);
        using var client = new HttpClient(handler);

        HttpResponseMessage a1 = await client.GetAsync("http://hosta.test/1");
        HttpResponseMessage a2 = await client.GetAsync("http://hosta.test/2");
        HttpResponseMessage b1 = await client.GetAsync("http://hostb.test/1");

        Assert.Equal(HttpStatusCode.OK, a1.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, a2.StatusCode);
        // host B must have its own budget and not be starved by traffic to host A.
        Assert.Equal(HttpStatusCode.OK, b1.StatusCode);
    }

    [Fact]
    public async Task SendAsync_QueueWait_IsNotChargedAgainstTheRequestTimeout()
    {
        // The rate-limit queue wait must not count against the per-request timeout: a request that is
        // only waiting for the next token replenishment is real progress, not a stuck network call. This
        // is the #31 download loop — large chapters (hundreds of images sharing one host's once-per-minute
        // burst) were aborting with TaskCanceledException and re-queueing forever, never writing the .cbz.
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };
        var inner = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        // One token per minute, room to queue, and a short request timeout. The handler owns the timeout
        // now, so it wraps only the network send — never the queue wait.
        using var handler = new RateLimitHandler(settings, inner, requestsPerMinute: 1, queueLimit: 10,
            requestTimeout: TimeSpan.FromMilliseconds(300));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        HttpResponseMessage first = await client.GetAsync("http://host.test/1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second request has no token and must wait ~1 minute for replenishment. The instant network plus
        // the 300ms request timeout must NOT cancel it — it is still pending well past that window.
        using var cts = new CancellationTokenSource();
        Task<HttpResponseMessage> second = client.GetAsync("http://host.test/2", cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(750));
        Assert.False(second.IsCompleted);

        // The worker token still cancels cooperatively — the wait honours real cancellation, just not the
        // request timeout.
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }

    [Fact]
    public async Task SendAsync_NetworkSend_TimesOutAfterTheRequestTimeout()
    {
        // The timeout still applies to the actual network send: a connection that hangs past the request
        // timeout is cancelled (the handler now owns this, since HttpClient.Timeout is disabled upstream).
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };
        var inner = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var handler = new RateLimitHandler(settings, inner, requestsPerMinute: 60, queueLimit: 10,
            requestTimeout: TimeSpan.FromMilliseconds(200));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        Task<HttpResponseMessage> call = client.GetAsync("http://host.test/1");
        Task finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(call, finished);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }
}
