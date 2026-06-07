using API.JobRuntime.Interfaces;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

[CollectionDefinition("postgres")]
public class PostgresCollectionDefinition { }

/// <summary>
/// Verifies that two concurrent Dispatcher instances claiming from the same Postgres database
/// each get at most one job — no double-execution.
/// Requires postgres from docker-compose.test.yml (skips if not reachable).
/// </summary>
[Collection("postgres")]
public class EfJobStoreConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg = new();
    private string? _dbName;
    private KenkuApplicationFactory? _app1;
    private KenkuApplicationFactory? _app2;

    public async Task InitializeAsync()
    {
        if (!await PostgresReachableAsync())
            return;
        _dbName = await _pg.CreateDatabaseAsync();
        string cs = _pg.GetConnectionString(_dbName);
        var handler = new CountingHandler(() => Interlocked.Increment(ref _executions));
        _app1 = new KenkuApplicationFactory { PostgresConnectionString = cs, ExtraJobHandlers = [handler] };
        _app2 = new KenkuApplicationFactory { PostgresConnectionString = cs, ExtraJobHandlers = [handler] };
    }

    public async Task DisposeAsync()
    {
        _app1?.Dispose();
        _app2?.Dispose();
        if (_dbName is not null)
            await _pg.DropDatabaseAsync(_dbName);
    }

    private int _executions;

    [Fact]
    public async Task ConcurrentDispatchers_ClaimSameJob_ExactlyOnce()
    {
        if (_app1 is null)
            return; // Postgres not available — skip

        using (var scope = _app1.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                new JobEntity("CountingJob", "{}", DateTime.UtcNow));

        var t1 = Task.Run(async () =>
        {
            using var scope = _app1.Services.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<API.JobRuntime.Dispatcher>();
            return await dispatcher.RunOnceAsync();
        });
        var t2 = Task.Run(async () =>
        {
            using var scope = _app2!.Services.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<API.JobRuntime.Dispatcher>();
            return await dispatcher.RunOnceAsync();
        });

        bool[] claimed = await Task.WhenAll(t1, t2);

        Assert.Equal(1, claimed.Count(c => c));
        Assert.Equal(1, _executions);
    }

    [Fact]
    public async Task MigrationsApplied_AllContextsHaveSchema()
    {
        if (_app1 is null)
            return; // Postgres not available — skip

        using var scope = _app1.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var jobsCtx = sp.GetRequiredService<global::API.Schema.JobsContext.JobsContext>();
        Assert.Empty(await jobsCtx.Database.GetPendingMigrationsAsync());

        var seriesCtx = sp.GetRequiredService<SeriesContext>();
        Assert.Empty(await seriesCtx.Database.GetPendingMigrationsAsync());
    }

    private static async Task<bool> PostgresReachableAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(PostgresFixture.AdminConnectionString);
            await conn.OpenAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class CountingHandler(Action onExecute) : IJobHandler
    {
        public string JobType => "CountingJob";
        public Task ExecuteAsync(JobEntity job, CancellationToken ct) { onExecute(); return Task.CompletedTask; }
    }
}
