namespace API.Discovery;

/// <summary>
/// AniList's fixed genre vocabulary (its GenreCollection). A genre rail only returns results for one
/// of these exact names, so the configured genres are constrained to this set — a free-text genre
/// like "Gore" would silently match nothing.
/// </summary>
public static class AniListGenres
{
    public static readonly IReadOnlyList<string> All =
    [
        "Action", "Adventure", "Comedy", "Drama", "Ecchi", "Fantasy", "Horror", "Mahou Shoujo",
        "Mecha", "Music", "Mystery", "Psychological", "Romance", "Sci-Fi", "Slice of Life",
        "Sports", "Supernatural", "Thriller",
    ];

    /// <summary>The canonical genre matching <paramref name="name"/> case-insensitively, or null if
    /// it is not an AniList genre.</summary>
    public static string? Canonical(string name) =>
        All.FirstOrDefault(g => g.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
}
