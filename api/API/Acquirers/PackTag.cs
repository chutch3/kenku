using System.Security.Cryptography;
using System.Text;

namespace API.Acquirers;

/// <summary>
/// Deterministic download-client tag for a pack release: every chapter that selects the same pack
/// computes the same tag, so the torrent is added to the client once and finalised once (fanning out
/// to all the chapters its files cover). The series key is encoded so completion can find the
/// chapters without re-searching the indexers.
/// </summary>
public static class PackTag
{
    private const string Prefix = "pack:";

    public static string For(string seriesKey, string downloadUrl)
    {
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(downloadUrl)));
        return $"{Prefix}{seriesKey}:{digest[..8].ToLowerInvariant()}";
    }

    /// <summary>The series key inside a pack tag, or null when <paramref name="tag"/> is not a pack tag.</summary>
    public static string? SeriesKeyOf(string tag)
    {
        if (!tag.StartsWith(Prefix, StringComparison.Ordinal))
            return null;
        int lastColon = tag.LastIndexOf(':');
        return lastColon > Prefix.Length ? tag[Prefix.Length..lastColon] : null;
    }
}
