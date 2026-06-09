using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.Requests;

/// <summary>How torrent releases are picked for comics (v1 scoring — not arr-style profiles).</summary>
public record ReleaseSelectionRecord(int MinSeeders, string[] PreferredTokens, string[] BlockedTokens)
{
    [Required]
    [Description("Releases with fewer seeders are never picked.")]
    public int MinSeeders { get; init; } = MinSeeders;

    [Required]
    [Description("Filename tokens that rank a release higher (e.g. cbz).")]
    public string[] PreferredTokens { get; init; } = PreferredTokens;

    [Required]
    [Description("Filename tokens that exclude a release outright (e.g. cbr, pdf).")]
    public string[] BlockedTokens { get; init; } = BlockedTokens;
}
