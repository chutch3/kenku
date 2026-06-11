using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.DTOs;

public sealed record SeriesSource(string Name, bool Enabled, string IconUrl, string[] SupportedLanguages, API.Acquirers.AcquisitionKind Kind, API.Connectors.ContentType ContentType) : Identifiable(Name)
{
    /// <summary>
    /// Whether Connector is used for Searches and Downloads
    /// </summary>
    [Required]
    [Description("Whether Connector is used for Searches and Downloads")]
    public bool Enabled { get; init; } = Enabled;

    /// <summary>
    /// Languages supported by the Connector
    /// </summary>
    [Required]
    [Description("Languages supported by the Connector")]
    public string[] SupportedLanguages { get; init; } = SupportedLanguages;

    /// <summary>
    /// Url of the Website Icon
    /// </summary>
    [Required]
    [Description("Url of the Website Icon")]
    public string IconUrl { get; init; } = IconUrl;

    /// <summary>
    /// How chapters from this source are acquired. Torrent-kind sources deliver comics via
    /// indexers + a download client; the UI diverges the comic experience on this.
    /// </summary>
    [Required]
    [Description("How chapters from this source are acquired; Torrent marks the comics/indexer path.")]
    public API.Acquirers.AcquisitionKind Kind { get; init; } = Kind;

    /// <summary>
    /// What the source serves (manga or comic), independent of how it acquires it. The UI diverges
    /// the comic experience on this.
    /// </summary>
    [Required]
    [Description("What the source serves (manga or comic), independent of how it acquires it.")]
    public API.Connectors.ContentType ContentType { get; init; } = ContentType;
}