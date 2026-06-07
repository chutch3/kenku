using API.JobRuntime.Interfaces;
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
/// Step-1 real-run gate (parallel strangler + smoke job): a job enqueued through the booted app's real
/// runtime (registry-validated trigger → EF job store → dispatcher → handler) runs to Succeeded, and an
/// unknown job type is rejected at the trust boundary (AF6b).
/// </summary>
public class JobRuntimeSmokeTests : IDisposable
{
    private sealed class SmokeHandler : IJobHandler
    {
        public string JobType => "smoke";
        public Task ExecuteAsync(JobEntity job, CancellationToken ct) => Task.CompletedTask;
    }

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly KenkuApplicationFactory _app;

    public JobRuntimeSmokeTests() =>
        _app = new KenkuApplicationFactory { OutboundHttpTarget = _server.Url!, ExtraJobHandlers = [new SmokeHandler()] };

    public void Dispose()
    {
        _app.Dispose();
        _server.Stop();
        GC.SuppressFinalize(this);
    }

    private static readonly JsonSerializerOptions Json = BuildJson();
    private static JsonSerializerOptions BuildJson()
    {
        var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    [Fact]
    public async Task EnqueuedJob_RunsToSucceeded_ThroughTheRealRuntime()
    {
        using var client = _app.CreateClient();

        var enqueue = await client.PostAsJsonAsync("/v2/JobQueue", new { type = "smoke", payload = "{}" });
        Assert.Equal(HttpStatusCode.Created, enqueue.StatusCode);
        var created = JsonSerializer.Deserialize<QueuedJob>(await enqueue.Content.ReadAsStringAsync(), Json)!;
        Assert.Equal(JobStatus.Queued, created.Status);

        // The pool is disabled under RunStartup=false, so drive one dispatcher tick explicitly.
        using (var scope = _app.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync());

        var status = JsonSerializer.Deserialize<QueuedJob>(
            await client.GetStringAsync($"/v2/JobQueue/{created.Key}"), Json)!;
        Assert.Equal(JobStatus.Succeeded, status.Status);
    }

    [Fact]
    public async Task Enqueue_UnknownType_IsRejected()
    {
        using var client = _app.CreateClient();
        var response = await client.PostAsJsonAsync("/v2/JobQueue", new { type = "ghost", payload = "{}" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
