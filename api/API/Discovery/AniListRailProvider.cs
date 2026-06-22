using API.Connectors;
using API.JobRuntime.Interfaces;

namespace API.Discovery;

/// <summary>
/// AniList's trending / new-this-year / top-rated shelves as Discover rails. AniList is a pure discovery
/// source (not a connector), so this is a standalone <see cref="IDiscoveryRailProvider"/> rather than a
/// SeriesSource. Genre rails stay their own bespoke path (they're a user-curated vocabulary).
/// </summary>
public class AniListRailProvider(IAniListClient aniList, IClock clock) : IDiscoveryRailProvider
{
    public IReadOnlyList<DiscoveryRail> Rails =>
    [
        new("manga-trending", "Trending", ContentType.Manga, 10),
        new("manga-new", "New & popular", ContentType.Manga, 40),
        new("manga-top-rated", "Top rated", ContentType.Manga, 50),
    ];

    public Task<List<DiscoveryEntry>> GetRailAsync(string railId, CancellationToken ct) => railId switch
    {
        "manga-trending" => aniList.GetMangaListAsync(AniListShelf.Trending, 20, ct),
        "manga-new" => aniList.GetMangaListAsync(AniListShelf.NewThisYear(clock.UtcNow.Year), 20, ct),
        "manga-top-rated" => aniList.GetMangaListAsync(AniListShelf.TopRated, 20, ct),
        _ => Task.FromResult(new List<DiscoveryEntry>()),
    };
}
