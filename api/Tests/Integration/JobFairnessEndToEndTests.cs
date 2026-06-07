using API.JobRuntime.Interfaces;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using API.Controllers.DTOs;
using API.JobRuntime;
using API.Schema.JobsContext;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Server;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

/// <summary>
/// AF2c fairness, outside-in: with more ready jobs for one resource than its per-resource cap, the real
/// dispatcher must not let that resource hold all in-flight slots — another resource gets a slot while the
/// surplus waits. Driven through the booted app's real dispatcher + EF store with a deterministic cap.
/// </summary>
public class JobFairnessEndToEndTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly GateHandler _gate = new();
    private readonly KenkuApplicationFactory _app;

    public JobFairnessEndToEndTests() =>
        _app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            ExtraJobHandlers = [_gate],
            // Plenty of global slots, but only ONE in-flight per resource — so fairness is observable.
            DispatcherCaps = (GlobalCap: 8, PerResourceCap: 1),
        };

    public void Dispose()
    {
        _app.Dispose();
        _server.Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>Records each started job's resource and blocks until released — so jobs stay in-flight
    /// while we observe what the dispatcher claims next.</summary>
    private sealed class GateHandler : IJobHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ConcurrentQueue<string> StartedResources = new();
        public string JobType => "gate";

        public async Task ExecuteAsync(JobEntity job, CancellationToken ct)
        {
            StartedResources.Enqueue(job.ResourceKey ?? "");
            await _release.Task.WaitAsync(ct);
        }

        public void ReleaseAll() => _release.TrySetResult();
    }

    private static readonly JsonSerializerOptions Json = BuildJson();
    private static JsonSerializerOptions BuildJson()
    {
        var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    private async Task Enqueue(HttpClient client, string resourceKey, int priority)
    {
        var response = await client.PostAsJsonAsync("/v2/JobQueue",
            new { type = "gate", payload = "{}", resourceKey, priority });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task<bool> RunOnceBackground() => Task.Run(async () =>
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync();
    });

    private async Task WaitForStarts(int n)
    {
        for (int i = 0; i < 100 && _gate.StartedResources.Count < n; i++)
            await Task.Delay(20);
        Assert.True(_gate.StartedResources.Count >= n, $"expected {n} started, saw {_gate.StartedResources.Count}");
    }

    [Fact]
    public async Task OneResourceCannotHoldAllSlots_AnotherResourceGetsASlot()
    {
        using var client = _app.CreateClient();
        // series-A's jobs even outrank series-B by priority — so a broken cap would deterministically claim
        // the surplus A second. Fairness must override priority: A is capped, so B gets the slot instead.
        await Enqueue(client, "series-A", priority: 1);   // A1
        await Enqueue(client, "series-A", priority: 1);   // A2
        await Enqueue(client, "series-B", priority: 0);   // B1

        // First tick claims an A (it's at cap 1 now). Second tick must skip A and claim B.
        var tick1 = RunOnceBackground();
        await WaitForStarts(1);
        var tick2 = RunOnceBackground();
        await WaitForStarts(2);

        // Both series are represented among the in-flight jobs — A did not take both slots. (If fairness
        // were broken, tick2 would have claimed the second A and this fails fast.)
        var started = _gate.StartedResources.ToArray();
        Assert.Equal(2, started.Length);
        Assert.Contains("series-A", started);
        Assert.Contains("series-B", started);

        // The surplus A is still waiting, not running.
        var all = JsonSerializer.Deserialize<List<QueuedJob>>(await client.GetStringAsync("/v2/JobQueue"), Json)!;
        Assert.Equal(1, all.Count(j => j is { ResourceKey: "series-A", Status: JobStatus.Queued }));

        // A is at its per-resource cap with one ready A still queued; a third claim finds nothing runnable.
        Assert.False(await RunOnceBackground());

        _gate.ReleaseAll();
        await Task.WhenAll(tick1, tick2);
    }
}
