using System.Text.Json;
using System.Text.Json.Serialization;
using API.Controllers.DTOs;
using API.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Step-0 Job Store: the worker queue persists every worker execution to the JobsContext, so status
/// survives restarts and repeated runs are observable — the capability the in-memory worker list lacked
/// when the #31 download loop ran silently for hours.
/// </summary>
public class JobRecordingTests : OutboundHttpIntegrationTest
{
    private sealed class CompletingWorker : BaseWorker
    {
        protected override Task<BaseWorker[]> DoWorkInternal() => Task.FromResult(Array.Empty<BaseWorker>());
    }

    private sealed class FailingWorker : BaseWorker
    {
        protected override Task<BaseWorker[]> DoWorkInternal() =>
            Task.FromException<BaseWorker[]>(new InvalidOperationException("boom"));
    }

    [Fact]
    public async Task CompletedWorker_IsPersistedAsCompletedJobRecord()
    {
        var queue = App.Services.GetRequiredService<IWorkerQueue>();
        var worker = new CompletingWorker();

        queue.AddWorker(worker);

        bool persisted = await WaitUntil(() => App.WithJobsContext(c => c.Jobs.AnyAsync(j =>
            j.Key == worker.Key && j.State == WorkerExecutionState.Completed && j.FinishedAt != null)));
        Assert.True(persisted, "a completed worker execution must be persisted as a Completed job record");
    }

    [Fact]
    public async Task FailedWorker_IsPersistedAsFailedJobRecord_WithError()
    {
        var queue = App.Services.GetRequiredService<IWorkerQueue>();
        var worker = new FailingWorker();

        queue.AddWorker(worker);

        bool persisted = await WaitUntil(() => App.WithJobsContext(c => c.Jobs.AnyAsync(j =>
            j.Key == worker.Key && j.State == WorkerExecutionState.Failed)));
        Assert.True(persisted, "a failed worker execution must be persisted as a Failed job record");

        var record = await App.WithJobsContext(c => c.Jobs.FirstAsync(j => j.Key == worker.Key));
        Assert.Contains("boom", record.Error);
    }

    [Fact]
    public async Task RepeatedExecutions_AreAllObservable_ViaTheJobsApi()
    {
        var queue = App.Services.GetRequiredService<IWorkerQueue>();
        var workers = new[] { new CompletingWorker(), new CompletingWorker(), new CompletingWorker() };

        foreach (var worker in workers)
            queue.AddWorker(worker);

        var keys = workers.Select(w => w.Key).ToHashSet();
        bool allDone = await WaitUntil(() => App.WithJobsContext(c =>
            c.Jobs.CountAsync(j => keys.Contains(j.Key) && j.State == WorkerExecutionState.Completed)
                .ContinueWith(t => t.Result == workers.Length)));
        Assert.True(allDone, "every repeated execution must be persisted, not just the currently-known one");

        var response = await App.CreateClient().GetAsync("/v2/Jobs");
        response.EnsureSuccessStatusCode();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var jobs = JsonSerializer.Deserialize<List<Job>>(await response.Content.ReadAsStringAsync(), options);
        Assert.NotNull(jobs);
        Assert.True(keys.IsSubsetOf(jobs!.Select(j => j.Key).ToHashSet()),
            "the jobs API must surface the persisted execution history");
    }
}
