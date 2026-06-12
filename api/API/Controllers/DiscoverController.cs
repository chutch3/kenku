using API.Connectors;
using API.Discovery;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace API.Controllers;

/// <summary>
/// Discovery rails: lists of series someone might want to add. Each rail is fetched from its third
/// party at most once per hour (<see cref="DiscoveryCache"/>) and degrades to the stale or empty
/// list instead of failing the page.
/// </summary>
[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class DiscoverController(DiscoveryCache cache, KenkuSettings settings, API.JobRuntime.Interfaces.IClock clock) : ControllerBase
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

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

    /// <summary>Fresh comics from the latest posts of archive sources (currently GetComics).</summary>
    /// <response code="200"></response>
    [HttpGet("Comics")]
    [ProducesResponseType<List<DiscoveryEntry>>(Status200OK, "application/json")]
    public async Task<Ok<List<DiscoveryEntry>>> GetFreshComics([FromServices] IEnumerable<SeriesSource> connectors)
        => TypedResults.Ok(await cache.GetOrRefreshAsync("fresh-comics", Ttl, async () =>
        {
            var entries = new List<DiscoveryEntry>();
            foreach (ILatestSeriesProvider provider in connectors.OfType<ILatestSeriesProvider>())
                entries.AddRange(await provider.GetLatestSeriesAsync(HttpContext.RequestAborted));
            return entries;
        }));

    /// <summary>Hot posts from the configured subreddits. One rate-limited subreddit never empties the rail.</summary>
    /// <response code="200"></response>
    [HttpGet("Feed")]
    [ProducesResponseType<List<DiscoveryEntry>>(Status200OK, "application/json")]
    public async Task<Ok<List<DiscoveryEntry>>> GetFeed([FromServices] IRedditFeedClient reddit)
        => TypedResults.Ok(await cache.GetOrRefreshAsync("reddit-feed", Ttl, async () =>
        {
            var entries = new List<DiscoveryEntry>();
            foreach (string subreddit in settings.DiscoveryFeeds)
            {
                try { entries.AddRange(await reddit.GetHotAsync(subreddit, 10, HttpContext.RequestAborted)); }
                catch (Exception) { /* cached/empty beats a dead rail; reddit 429s freely */ }
            }
            return entries;
        }));
}
