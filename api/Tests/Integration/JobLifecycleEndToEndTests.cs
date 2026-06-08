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
/// Outside-in coverage for the runtime's safety/control guarantees through the booted app (real
/// dispatcher + EF job store + JobQueue API): AF2c (bounded retry → NeedsAttention, stops re-queuing),
/// AF6c (retry re-arms), AF6d (cancel a Queued or a Running job → Cancelled, no post-cancel side effect).
/// </summary>
public class JobLifecycleEndToEndTests : IAsyncLifetime
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly FakeClock _clock = new();
    private readonly BoomHandler _boom = new();
    private readonly BlockingHandler _block = new();
    private readonly PostgresFixture _postgres = new();
    private string? _dbName;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        _app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            Clock = _clock,
            ExtraJobHandlers = [_boom, _block],
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
        };
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        _server.Stop();
        if (_dbName is not null)
            await _postgres.DropDatabaseAsync(_dbName);
    }

    /// <summary>Always fails — drives the bounded-retry path to NeedsAttention.</summary>
    private sealed class BoomHandler : IJobHandler
    {
        public string JobType => "boom";
        public Task ExecuteAsync(JobEntity job, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    /// <summary>Signals when it starts, then blocks on the token. <see cref="CompletedNormally"/> is only
    /// set if the wait is NOT cancelled — so a cancelled run leaves it false (no post-cancel side effect).</summary>
    private sealed class BlockingHandler : IJobHandler
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool CompletedNormally;
        public string JobType => "block";

        public async Task ExecuteAsync(JobEntity job, CancellationToken ct)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            CompletedNormally = true;
        }
    }

    private static readonly JsonSerializerOptions Json = BuildJson();
    private static JsonSerializerOptions BuildJson()
    {
        var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    private async Task<QueuedJob> Enqueue(HttpClient client, string type)
    {
        var response = await client.PostAsJsonAsync("/v2/JobQueue", new { type, payload = "{}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonSerializer.Deserialize<QueuedJob>(await response.Content.ReadAsStringAsync(), Json)!;
    }

    private async Task<QueuedJob> Get(HttpClient client, string key) =>
        JsonSerializer.Deserialize<QueuedJob>(await client.GetStringAsync($"/v2/JobQueue/{key}"), Json)!;

    private async Task<bool> RunOnce()
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync();
    }

    [Fact]
    public async Task FailingJob_ReachesNeedsAttention_StopsRequeuing_ThenRetryReArms()
    {
        using var client = _app.CreateClient();
        var job = await Enqueue(client, "boom");

        // Drive the bounded-retry loop: each failure reschedules with backoff, so advance the clock past it.
        int runs = 0;
        for (int i = 0; i < 20; i++)
        {
            if (!await RunOnce()) break;
            runs++;
            if ((await Get(client, job.Key)).Status != JobStatus.Queued) break;
            _clock.Advance(TimeSpan.FromHours(1)); // past any backoff window
        }

        var failed = await Get(client, job.Key);
        Assert.Equal(JobStatus.NeedsAttention, failed.Status);
        Assert.Equal(failed.MaxAttempts, failed.Attempts);   // bounded — exactly MaxAttempts tries
        Assert.Equal(failed.MaxAttempts, runs);              // and no more
        Assert.NotNull(failed.Error);

        // Stops re-queuing: a NeedsAttention job is not claimable, no matter how far time advances.
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await RunOnce());

        // AF6c: retry re-arms it (Queued, attempts reset).
        var retried = await client.PostAsync($"/v2/JobQueue/{job.Key}/Retry", null);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var reArmed = await Get(client, job.Key);
        Assert.Equal(JobStatus.Queued, reArmed.Status);
        Assert.Equal(0, reArmed.Attempts);
        Assert.True(await RunOnce()); // claimable again
    }

    [Fact]
    public async Task DismissNeedsAttentionJob_RemovesItFromTheQueue()
    {
        using var client = _app.CreateClient();
        var job = await Enqueue(client, "boom");

        for (int i = 0; i < 20; i++)
        {
            if (!await RunOnce()) break;
            if ((await Get(client, job.Key)).Status != JobStatus.Queued) break;
            _clock.Advance(TimeSpan.FromHours(1));
        }
        Assert.Equal(JobStatus.NeedsAttention, (await Get(client, job.Key)).Status);

        var dismissed = await client.PostAsync($"/v2/JobQueue/{job.Key}/Dismiss", null);
        Assert.Equal(HttpStatusCode.NoContent, dismissed.StatusCode);

        var lookup = await client.GetAsync($"/v2/JobQueue/{job.Key}");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task DismissActiveJob_IsRejected_SoCancelIsUsedInstead()
    {
        using var client = _app.CreateClient();
        var job = await Enqueue(client, "block"); // stays Queued (never run here)

        var dismissed = await client.PostAsync($"/v2/JobQueue/{job.Key}/Dismiss", null);
        Assert.Equal(HttpStatusCode.PreconditionFailed, dismissed.StatusCode);
        Assert.Equal(JobStatus.Queued, (await Get(client, job.Key)).Status); // still there
    }

    [Fact]
    public async Task CancelQueuedJob_TransitionsToCancelled_WithoutRunning()
    {
        using var client = _app.CreateClient();
        var job = await Enqueue(client, "block");

        var cancel = await client.PostAsync($"/v2/JobQueue/{job.Key}/Cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        Assert.Equal(JobStatus.Cancelled, (await Get(client, job.Key)).Status);
        Assert.False(_block.Started.Task.IsCompleted, "a cancelled-while-Queued job must never start");
    }

    [Fact]
    public async Task CancelRunningJob_StopsCooperatively_RecordsCancelled_NoSideEffect()
    {
        using var client = _app.CreateClient();
        var job = await Enqueue(client, "block");

        // Run the handler on a background tick; it blocks until cancelled.
        var tick = Task.Run(RunOnce);
        Assert.True(await Task.WhenAny(_block.Started.Task, Task.Delay(5000)) == _block.Started.Task,
            "handler should have started running");

        var cancel = await client.PostAsync($"/v2/JobQueue/{job.Key}/Cancel", null);
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode); // 202: a running job was signalled

        Assert.True(await tick); // the dispatcher tick completes once the handler honours the token

        Assert.Equal(JobStatus.Cancelled, (await Get(client, job.Key)).Status);
        Assert.False(_block.CompletedNormally, "post-cancel handler code must not run (no partial side effect)");
    }
}
