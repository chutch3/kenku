namespace API.Discovery;

/// <summary>One discovery-rail card: a series someone might want to add, regardless of which rail found it.</summary>
public record DiscoveryEntry(string Title, string CoverUrl, string Url, string Source, string? Blurb);
