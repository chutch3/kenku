using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using API.Connectors;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using API.Schema.SeriesContext.MetadataFetchers;
using log4net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection; // Required for GetRequiredService

namespace API;

// 1. Removed 'static' from the class definition
public class Kenku
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(Kenku));

    private readonly IServiceProvider _serviceProvider;
    private readonly RateLimitHandler _rateLimitHandler;
    private readonly KenkuSettings _settings;

    public IEnumerable<SeriesSource> Connectors { get; }
    public IEnumerable<MetadataFetcher> MetadataFetchers { get; }

    public Kenku(
        IServiceProvider serviceProvider,
        IEnumerable<SeriesSource> connectors,
        IEnumerable<MetadataFetcher> fetchers,
        RateLimitHandler rateLimitHandler,
        KenkuSettings settings)
    {
        _serviceProvider = serviceProvider;
        _settings = settings;
        _rateLimitHandler = rateLimitHandler;
        Connectors = connectors;
        MetadataFetchers = fetchers;
    }

    internal bool TryGetSeriesSource(string name, [NotNullWhen(true)]out SeriesSource? seriesSource)
    {
        seriesSource = Connectors.FirstOrDefault(c => c.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
        return seriesSource != null;
    }

    // 5. Removed 'this' from SeriesContext. It is now just a normal method you call on Kenku.
    internal async Task<(Series manga, SourceId<Series> id)?> AddMangaToContext(SeriesContext context, (Series, SourceId<Series>) addManga, CancellationToken token) =>
        await AddMangaToContext(context, addManga.Item1, addManga.Item2, token);

    internal async Task<(Series manga, SourceId<Series> id)?> AddMangaToContext(SeriesContext context, Series addManga, SourceId<Series> addMcId, CancellationToken token)
    {
        context.ChangeTracker.Clear();
        Log.DebugFormat("Adding Series to Context: {0}", addManga);
        (Series, SourceId<Series>)? result;
        if (await context.FindMangaLike(addManga, token) is { } mangaId)
        {
            Series manga = await context.MangaIncludeAll().FirstAsync(m => m.Key == mangaId, token);
            Log.DebugFormat("Merging with existing Series: {0}", manga);

            var existingMcId = manga.SourceIds
                .FirstOrDefault(id => id.MangaConnectorName == addMcId.MangaConnectorName
                                      && id.IdOnConnectorSite == addMcId.IdOnConnectorSite);

            SourceId<Series> mcIdToUse;
            if (existingMcId == null)
            {
                mcIdToUse = new SourceId<Series>(manga, addMcId.MangaConnectorName, addMcId.IdOnConnectorSite, addMcId.WebsiteUrl, addMcId.UseForDownload);
                manga.SourceIds.Add(mcIdToUse);
                Log.DebugFormat("Added new SourceId for {0}", addMcId.MangaConnectorName);
            }
            else
            {
                mcIdToUse = existingMcId;
                if (existingMcId.WebsiteUrl != addMcId.WebsiteUrl)
                {
                    var updatedMcId = new SourceId<Series>(manga, existingMcId.MangaConnectorName, existingMcId.IdOnConnectorSite, addMcId.WebsiteUrl, existingMcId.UseForDownload);
                    manga.SourceIds.Remove(existingMcId);
                    manga.SourceIds.Add(updatedMcId);
                    mcIdToUse = updatedMcId;
                    Log.DebugFormat("Updated/Recreated SourceId for {0} (URL changed)", addMcId.MangaConnectorName);
                }
            }

            result = (manga, mcIdToUse);
        }
        else
        {
            Log.Debug("Series does not exist yet.");
            IEnumerable<SeriesTag> mergedTags = addManga.MangaTags.Select(mt =>
            {
                SeriesTag? inDb = context.Tags.Find(mt.Tag);
                return inDb ?? mt;
            });
            addManga.MangaTags = mergedTags.ToList();

            IEnumerable<Author> mergedAuthors = addManga.Authors.Select(ma =>
            {
                Author? inDb = context.Authors.Find(ma.Key);
                return inDb ?? ma;
            });
            addManga.Authors = mergedAuthors.ToList();

            context.Series.Add(addManga);
            context.Set<SourceId<Series>>().Add(addMcId);
            result = (addManga, addMcId);
        }

        if (await context.Sync(token, reason: "AddMangaToContext") is { success: false })
            return null;

        using (IServiceScope scope = _serviceProvider.CreateScope())
        {
            var jobStore = scope.ServiceProvider.GetRequiredService<JobRuntime.Interfaces.IJobStore>();
            var clock = scope.ServiceProvider.GetRequiredService<JobRuntime.Interfaces.IClock>();
            await jobStore.EnqueueAsync(new Schema.JobsContext.Job(
                JobRuntime.Handlers.DownloadCoverHandler.Type,
                JobRuntime.Handlers.DownloadCoverHandler.PayloadFor(result.Value.Item2.Key), clock.UtcNow,
                resourceKey: result.Value.Item2.ObjId,
                dedupKey: JobRuntime.Reconcilers.CoverRefreshReconciler.DedupKey(result.Value.Item2.Key)), token);
        }

        return result;
    }
}
