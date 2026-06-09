using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.Requests;

/// <summary>The corrected connector entry a series' source link should point at.</summary>
public record RematchSourceRecord
{
    [Required]
    [Description("The series' id on the connector site (from a connector search result).")]
    public required string IdOnConnectorSite { get; init; }

    [Description("The series' page URL on the connector site, if known.")]
    public string? WebsiteUrl { get; init; }
}
