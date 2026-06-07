using log4net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime;

/// <summary>
/// The generic worker pool: N concurrent loops that each claim and run one ready job per tick via a fresh
/// scope (so each job gets its own DbContexts), idling when the queue is empty. Disabled when startup is
/// off (integration tests drive the dispatcher explicitly).
/// </summary>
public class JobPoolService(IServiceScopeFactory scopeFactory, KenkuSettings settings, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(JobPoolService));
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Kenku:RunStartup", true))
            return;

        int concurrency = Math.Max(1, settings.MaxConcurrentWorkers);
        Log.InfoFormat("Starting job pool with {0} workers.", concurrency);
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => Loop(stoppingToken)));
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool ranJob = false;
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                ranJob = await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                Log.Error("Job pool loop error", e);
            }

            if (!ranJob)
                await Task.Delay(IdleDelay, ct);
        }
    }
}
