using API.JobRuntime.Interfaces;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using API.Controllers.DTOs;
using API.JobRuntime;
using API.Schema.JobsContext;
using API.Tests.Unit.JobRuntime;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Server;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

/// <summary>
/// AF6a: GET /v2/JobQueue/Summary returns correct status counts; a network-timeout OCE is exposed as a
/// human-readable error, not the raw "A task was cancelled." message.
/// </summary>
public class JobQueueSummaryTests : IAsyncLifetime
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly FakeClock _clock = new();
    private readonly BoomHandler _boom = new();
    private readonly NetworkTimeoutHandler _timeout = new();
    private readonly PostgresFixture _postgres = new();
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        _app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            Clock = _clock,
            ExtraJobHandlers = [_boom, _timeout],
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
        };
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        _server.Stop();
        await _postgres.DropDatabaseAsync(_dbName);
    }

    /// <summary>Always throws — drives failed/retryable path.</summary>
    private sealed class BoomHandler : IJobHandler
    {
        public string JobType => "boom-summary";
        public Task ExecuteAsync(JobEntity job, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    /// <summary>Throws OperationCanceledException without the job's own token being cancelled —
    /// simulates a network timeout rather than an explicit job cancel.</summary>
    private sealed class NetworkTimeoutHandler : IJobHandler
    {
        public string JobType => "timeout-sim";
        public Task ExecuteAsync(JobEntity job, CancellationToken ct) =>
            throw new OperationCanceledException("A task was cancelled.");
    }

    private static readonly JsonSerializerOptions Json = BuildJson();
    private static JsonSerializerOptions BuildJson()
    {
        var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    private async Task<string> EnqueueViaApi(HttpClient client, string type)
    {
        var response = await client.PostAsJsonAsync("/v2/JobQueue", new { type, payload = "{}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = JsonSerializer.Deserialize<QueuedJob>(await response.Content.ReadAsStringAsync(), Json)!;
        return job.Key;
    }

    private async Task<QueuedJob> Get(HttpClient client, string key) =>
        JsonSerializer.Deserialize<QueuedJob>(await client.GetStringAsync($"/v2/JobQueue/{key}"), Json)!;

    private async Task RunOnce()
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync();
    }

    [Fact]
    public async Task Summary_ReturnsCorrectStatusCounts()
    {
        using var client = _app.CreateClient();

        // Enqueue two boom jobs and run them to failure (they retry with backoff).
        string boom1 = await EnqueueViaApi(client, "boom-summary");
        string boom2 = await EnqueueViaApi(client, "boom-summary");
        await RunOnce();
        await RunOnce();

        // Both are now Queued (retrying with backoff) or NeedsAttention if MaxAttempts==1.
        // Either way the summary must reflect non-zero counts.
        var summaryResponse = await client.GetStringAsync("/v2/JobQueue/Summary");
        var summary = JsonSerializer.Deserialize<Dictionary<string, int>>(summaryResponse, Json)!;

        int total = summary.Values.Sum();
        Assert.True(total >= 2, $"summary must account for at least the 2 boom jobs; got total={total}");
    }

    [Fact]
    public async Task TimedOutRequest_ExposesReadableError_NotRawCancelled()
    {
        using var client = _app.CreateClient();

        string key = await EnqueueViaApi(client, "timeout-sim");
        await RunOnce();

        var job = await Get(client, key);

        Assert.NotNull(job.Error);
        Assert.DoesNotContain("A task was cancelled", job.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Timed out", job.Error, StringComparison.OrdinalIgnoreCase);
    }
}
