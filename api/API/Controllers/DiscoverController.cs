using API.Connectors;
using API.Discovery;
using API.Schema.DiscoveryContext;
using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace API.Controllers;

/// <summary>
/// Discovery rails: lists of series someone might want to add. AniList and GetComics rails are
/// fetched from their third party at most once per hour (<see cref="DiscoveryCache"/>); the feed
/// rail reads the database cache kept fresh by
/// <see cref="API.JobRuntime.Handlers.RefreshDiscoveryFeedHandler"/>. Every rail degrades to the
/// stale or empty list instead of failing the page.
/// </summary>
[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class DiscoverController(DiscoveryCache cache, KenkuSettings settings, API.JobRuntime.Interfaces.IClock clock) : ControllerBase
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    /// <summary>AniList's supported genres — the set a genre rail can be configured for.</summary>
    /// <response code="200"></response>
    [HttpGet("Genres")]
    [ProducesResponseType<IReadOnlyList<string>>(Status200OK, "application/json")]
    public Ok<IReadOnlyList<string>> GetGenres() => TypedResults.Ok(AniListGenres.All);

    /// <summary>Manga trending on AniList right now.</summary>
    /// <response code="200"></response>
    [HttpGet("Manga")]
    [ProducesResponseType<List<DiscoveryEntry>>(Status200OK, "application/json")]
    public async Task<Ok<List<DiscoveryEntry>>> GetTrendingManga([FromServices] IAniListClient aniList)
        => TypedResults.Ok(await cache.GetOrRefreshAsync("anilist-trending", Ttl,
            () => aniList.GetMangaListAsync(AniListShelf.Trending, 20, HttpContext.RequestAborted)));

    /// <summary>All-time highest-rated manga on AniList.</summary>
    /// <response code="200"></response>
    [HttpGet("Manga/TopRated")]
    [ProducesResponseType<List<DiscoveryEntry>>(Status200OK, "application/json")]
    public async Task<Ok<List<DiscoveryEntry>>> GetTopRatedManga([FromServices] IAniListClient aniList)
        => TypedResults.Ok(await cache.GetOrRefreshAsync("anilist-top-rated", Ttl,
            () => aniList.GetMangaListAsync(AniListShelf.TopRated, 20, HttpContext.RequestAborted)));

    /// <summary>Popular manga that started this year — the manga stand-in for a seasonal shelf.</summary>
    /// <response code="200"></response>
    [HttpGet("Manga/New")]
    [ProducesResponseType<List<DiscoveryEntry>>(Status200OK, "application/json")]
    public async Task<Ok<List<DiscoveryEntry>>> GetNewManga([FromServices] IAniListClient aniList)
    {
        int year = clock.UtcNow.Year;
        return TypedResults.Ok(await cache.GetOrRefreshAsync($"anilist-new-{year}", Ttl,
            () => aniList.GetMangaListAsync(AniListShelf.NewThisYear(year), 20, HttpContext.RequestAborted)));
    }

    /// <summary>Manga trending in one of the configured genres (<see cref="KenkuSettings.DiscoveryGenres"/>).</summary>
    /// <response code="200"></response>
    /// <response code="404">The genre is not configured — keeps the cache bounded to known rails.</response>
    [HttpGet("Manga/Genre/{Genre}")]
    [ProducesResponseType<List<DiscoveryEntry>>(Status200OK, "application/json")]
    [ProducesResponseType(Status404NotFound)]
    public async Task<Results<Ok<List<DiscoveryEntry>>, NotFound>> GetGenreManga(string Genre, [FromServices] IAniListClient aniList)
    {
        string? genre = settings.DiscoveryGenres.FirstOrDefault(g => g.Equals(Genre, StringComparison.OrdinalIgnoreCase));
        if (genre is null)
            return TypedResults.NotFound();
        return TypedResults.Ok(await cache.GetOrRefreshAsync($"anilist-genre-{genre.ToLowerInvariant()}", Ttl,
            () => aniList.GetMangaListAsync(AniListShelf.ForGenre(genre), 20, HttpContext.RequestAborted)));
    }

    /// <summary>
    /// Every enabled Discover rail — all providers' declared rails minus the <see cref="KenkuSettings.DiscoveryRails"/>
    /// denylist — in fixed global order, each with its entries. The data-driven endpoint the page renders
    /// from; adding a rail is provider-only work. Genre rails and the reddit feed remain their own endpoints.
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("Rails")]
    [ProducesResponseType<List<DiscoveryRailResponse>>(Status200OK, "application/json")]
    public async Task<Ok<List<DiscoveryRailResponse>>> GetRails(
        [FromServices] IEnumerable<SeriesSource> connectors,
        [FromServices] IEnumerable<IDiscoveryRailProvider> standaloneProviders)
    {
        var enabled = connectors.OfType<IDiscoveryRailProvider>().Concat(standaloneProviders).Distinct()
            .SelectMany(p => p.Rails.Select(rail => (Provider: p, Rail: rail)))
            .Where(x => !settings.DiscoveryRails.Contains(x.Rail.Id))
            .OrderBy(x => x.Rail.Order)
            .ToList();

        var result = new List<DiscoveryRailResponse>();
        foreach (var (provider, rail) in enabled)
        {
            List<DiscoveryEntry> entries = await cache.GetOrRefreshAsync($"rail-{rail.Id}", Ttl,
                () => provider.GetRailAsync(rail.Id, HttpContext.RequestAborted));
            result.Add(new DiscoveryRailResponse(rail.Id, rail.Label, rail.ContentType, entries));
        }
        return TypedResults.Ok(result);
    }

    /// <summary>Fresh comics from the latest posts of archive sources (currently GetComics).</summary>
    /// <response code="200"></response>
    [HttpGet("Comics")]
    [ProducesResponseType<List<DiscoveryEntry>>(Status200OK, "application/json")]
    public async Task<Ok<List<DiscoveryEntry>>> GetFreshComics([FromServices] IEnumerable<SeriesSource> connectors)
        => TypedResults.Ok(await cache.GetOrRefreshAsync("fresh-comics", Ttl, async () =>
        {
            var entries = new List<DiscoveryEntry>();
            foreach (IDiscoveryRailProvider provider in connectors.OfType<IDiscoveryRailProvider>())
                foreach (DiscoveryRail rail in provider.Rails.Where(r => r.ContentType == ContentType.Comic))
                    entries.AddRange(await provider.GetRailAsync(rail.Id, HttpContext.RequestAborted));
            return entries;
        }));

    /// <summary>
    /// Hot posts from the configured subreddits, served from the database cache kept fresh by
    /// <see cref="API.JobRuntime.Handlers.RefreshDiscoveryFeedHandler"/> — so the rail survives
    /// reddit rate-limiting with its last good batch.
    /// </summary>
    /// <response code="200"></response>
    [HttpGet("Feed")]
    [ProducesResponseType<List<DiscoveryEntry>>(Status200OK, "application/json")]
    public async Task<Ok<List<DiscoveryEntry>>> GetFeed([FromServices] DiscoveryContext db)
    {
        var posts = await db.Posts.ToListAsync(HttpContext.RequestAborted);
        return TypedResults.Ok(settings.DiscoveryFeeds
            .SelectMany(rail => posts
                .Where(p => p.Rail.Equals(rail, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Position))
            .Select(p => new DiscoveryEntry(p.Title, p.CoverUrl, p.Url, p.Source, p.Blurb))
            .ToList());
    }
}
