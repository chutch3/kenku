namespace API.Discovery;

/// <summary>One AniList manga shelf: a sort order plus optional filters.</summary>
public record AniListShelf(string Sort, string? Genre = null, int? MinStartYear = null)
{
    public static readonly AniListShelf Trending = new("TRENDING_DESC");
    public static readonly AniListShelf TopRated = new("SCORE_DESC");

    /// <summary>Popular manga that started in <paramref name="year"/> — the manga stand-in for a
    /// seasonal shelf (AniList seasons only exist for anime).</summary>
    public static AniListShelf NewThisYear(int year) => new("POPULARITY_DESC", MinStartYear: year);

    public static AniListShelf ForGenre(string genre) => new("TRENDING_DESC", Genre: genre);
}
