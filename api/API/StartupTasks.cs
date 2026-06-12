using API.Connectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.LibraryContext;
using API.Schema.NotificationsContext;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API;

/// <summary>
/// Production boot work, gated behind Kenku:RunStartup (integration tests host the app with the flag
/// off so they can use an in-memory DB and drive jobs explicitly): apply migrations on every context,
/// seed first-run defaults, and apply the persisted connector enable/disable state.
/// </summary>
public static class StartupTasks
{
    /// <summary>Returns false when migrations fail — the app must not start against a broken schema.</summary>
    public static async Task<bool> MigrateAndSeedAsync(WebApplication app, KenkuSettings settings, ILog log)
    {
        try
        {
            log.Debug("Applying Migrations...");
            using (IServiceScope scope = app.Services.CreateScope())
            {
                SeriesContext context = scope.ServiceProvider.GetRequiredService<SeriesContext>();
                await context.Database.MigrateAsync(CancellationToken.None);

                if (!await context.FileLibraries.AnyAsync())
                {
                    await context.FileLibraries.AddAsync(new(settings.DefaultDownloadLocation, "Default FileLibrary"),
                        CancellationToken.None);

                    if (await context.Sync(CancellationToken.None, reason: "Add default library") is { success: false } contextException)
                        log.ErrorFormat("Failed to save database changes: {0}", contextException.exceptionMessage);
                }
            }

            using (IServiceScope scope = app.Services.CreateScope())
            {
                NotificationsContext context = scope.ServiceProvider.GetRequiredService<NotificationsContext>();
                await context.Database.MigrateAsync(CancellationToken.None);

                int deleted = await context.Notifications.ExecuteDeleteAsync(CancellationToken.None);
                log.DebugFormat("Deleted {0} old notifications.", deleted);
                string[] emojis =
                [
                    "(•‿•)", "(づ ◕‿◕ )づ", "( ˘▽˘)っ♨", "=＾● ⋏ ●＾=",
                    "（ΦωΦ）", "(✪㉨✪)", "( ﾉ･o･ )ﾉ", "（〜^∇^ )〜", "~(≧ω≦)~", "૮ ´• ﻌ ´• ა",
                    "(˃ᆺ˂)", "(=🝦 ༝ 🝦=)"
                ];
                await context.Notifications.AddAsync(
                    new("Kenku Started", emojis.RandomElement(), NotificationUrgency.High),
                    CancellationToken.None);

                if (await context.Sync(CancellationToken.None, reason: "Startup notification") is { success: false } contextException)
                    log.ErrorFormat("Failed to save database changes: {0}", contextException.exceptionMessage);
            }

            using (IServiceScope scope = app.Services.CreateScope())
            {
                LibraryContext context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
                await context.Database.MigrateAsync(CancellationToken.None);

                await context.Sync(CancellationToken.None, reason: "Startup library");
            }

            using (IServiceScope scope = app.Services.CreateScope())
            {
                ActionsContext context = scope.ServiceProvider.GetRequiredService<ActionsContext>();
                await context.Database.MigrateAsync(CancellationToken.None);
                context.Actions.Add(new StartupActionRecord());

                if (await context.Sync(CancellationToken.None, reason: "Startup actions") is { success: false } contextException)
                    log.ErrorFormat("Failed to save database changes: {0}", contextException.exceptionMessage);
            }

            using (IServiceScope scope = app.Services.CreateScope())
            {
                Schema.JobsContext.JobsContext context = scope.ServiceProvider.GetRequiredService<Schema.JobsContext.JobsContext>();
                await context.Database.MigrateAsync(CancellationToken.None);
            }

            using (IServiceScope scope = app.Services.CreateScope())
            {
                Schema.DiscoveryContext.DiscoveryContext context = scope.ServiceProvider.GetRequiredService<Schema.DiscoveryContext.DiscoveryContext>();
                await context.Database.MigrateAsync(CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            log.Fatal("Migrations failed!", e);
            return false;
        }

        log.Info("Starting Kenku.");

        // Apply persisted connector enable/disable state from settings
        var kenkuSettings = app.Services.GetRequiredService<KenkuSettings>();
        var mangaConnectors = app.Services.GetRequiredService<IEnumerable<SeriesSource>>();
        kenkuSettings.ApplyDisabledConnectors(mangaConnectors);
        return true;
    }
}
