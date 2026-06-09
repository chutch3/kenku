using API.JobRuntime.Interfaces;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

[CollectionDefinition("postgres")]
public class PostgresCollectionDefinition { }

/// <summary>
/// Verifies that two concurrent Dispatcher instances claiming from the same Postgres database
/// each get at most one job — no double-execution.
/// Requires Postgres running via docker-compose.test.yml.
/// </summary>
[Collection("postgres")]
public class EfJobStoreConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    private string _dbName = null!;
    private KenkuApplicationFactory _app1 = null!;
    private KenkuApplicationFactory _app2 = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _pg.CreateDatabaseAsync();
        string cs = _pg.GetConnectionString(_dbName);
        var handler = new CountingHandler(() => Interlocked.Increment(ref _executions));
        _app1 = new KenkuApplicationFactory { PostgresConnectionString = cs, ExtraJobHandlers = [handler] };
        _app2 = new KenkuApplicationFactory { PostgresConnectionString = cs, ExtraJobHandlers = [handler] };
    }

    public async Task DisposeAsync()
    {
        _app1.Dispose();
        _app2.Dispose();
        await _pg.DropDatabaseAsync(_dbName);
    }

    private int _executions;

    [Fact]
    public async Task ConcurrentDispatchers_ClaimSameJob_ExactlyOnce()
    {
        using (var scope = _app1.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                new JobEntity("CountingJob", "{}", DateTime.UtcNow));

        var t1 = Task.Run(async () =>
        {
            using var scope = _app1.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<API.JobRuntime.Dispatcher>().RunOnceAsync();
        });
        var t2 = Task.Run(async () =>
        {
            using var scope = _app2.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<API.JobRuntime.Dispatcher>().RunOnceAsync();
        });

        bool[] claimed = await Task.WhenAll(t1, t2);

        Assert.Equal(1, claimed.Count(c => c));
        Assert.Equal(1, _executions);
    }

    [Fact]
    public async Task Enqueue_CoalescesOntoANeedsAttentionJob()
    {
        using var scope = _app1.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var first = await store.EnqueueAsync(new JobEntity("CountingJob", "{}", DateTime.UtcNow, dedupKey: "d"));
        first.Status = API.Schema.JobsContext.JobStatus.NeedsAttention;
        await store.UpdateAsync(first);

        var second = await store.EnqueueAsync(new JobEntity("CountingJob", "{}", DateTime.UtcNow, dedupKey: "d"));

        Assert.Equal(first.Key, second.Key);
    }

    [Fact]
    public async Task MigrationsApplied_AllContextsHaveSchema()
    {
        using var scope = _app1.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var jobsCtx = sp.GetRequiredService<global::API.Schema.JobsContext.JobsContext>();
        Assert.Empty(await jobsCtx.Database.GetPendingMigrationsAsync());

        var seriesCtx = sp.GetRequiredService<SeriesContext>();
        Assert.Empty(await seriesCtx.Database.GetPendingMigrationsAsync());
    }

    private sealed class CountingHandler(Action onExecute) : IJobHandler
    {
        public string JobType => "CountingJob";
        public Task ExecuteAsync(JobEntity job, CancellationToken ct) { onExecute(); return Task.CompletedTask; }
    }
}
