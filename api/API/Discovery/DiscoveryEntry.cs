namespace API.Discovery;

/// <summary>One discovery-rail card: a series someone might want to add, regardless of which rail found it.
/// <paramref name="Tags"/> is optional (genres/themes) — providers that have them populate it for the card
/// chips; sources without tags (GetComics, reddit) leave it null.</summary>
public record DiscoveryEntry(string Title, string CoverUrl, string Url, string Source, string? Blurb,
    IReadOnlyList<string>? Tags = null);
