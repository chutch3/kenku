using log4net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// The shared loop for every hosted reconciler: gated off under test (Kenku:RunStartup=false), one
/// fresh DI scope per tick so each pass gets its own DbContexts, errors logged but never fatal, then
/// sleep for the subclass's interval. Subclasses implement a single tick — typically delegating to
/// their static, separately-tested ScanAndEnqueueAsync.
/// </summary>
public abstract class Reconciler(IServiceScopeFactory scopeFactory, IConfiguration configuration) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }

    /// <summary>One pass over a fresh scope. Exceptions are logged and the loop continues.</summary>
    protected abstract Task TickAsync(IServiceProvider scope, CancellationToken ct);

    /// <summary>Extra enablement gate beyond RunStartup (e.g. a feature flag); false disables the loop.</summary>
    protected virtual bool Enabled => true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Kenku:RunStartup", true) || !Enabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                await TickAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { LogManager.GetLogger(GetType()).Error($"{GetType().Name} error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
