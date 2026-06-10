using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using API.Schema.SeriesContext;

namespace API.Controllers.DTOs;

/// <summary>
/// <see cref="SourceId{T}"/> DTO. Field names mirror the schema entity exactly — ObjId is the key of
/// the referenced object, IdOnConnectorSite is the object's id on the connector site. (They were
/// historically swapped on the wire, which made connector-id bugs invisible.)
/// </summary>
public sealed record SourceId<T>(string Key, string MangaConnectorName, string ObjId, string IdOnConnectorSite, string? WebsiteUrl, bool UseForDownload) : Identifiable(Key) where T : class
{
    /// <summary>
    /// Name of the Connector
    /// </summary>
    [Required]
    [Description("Name of the Connector")]
    public string MangaConnectorName { get; init; } = MangaConnectorName;

    /// <summary>
    /// Key of the referenced object (series or chapter)
    /// </summary>
    [Required]
    [Description("Key of the referenced object (series or chapter)")]
    public string ObjId { get; init; } = ObjId;

    /// <summary>
    /// ID of the object on the connector site
    /// </summary>
    [Required]
    [Description("ID of the object on the connector site")]
    public string IdOnConnectorSite { get; init; } = IdOnConnectorSite;

    /// <summary>
    /// Website Link for reference, if any
    /// </summary>
    [Description("Website Link for reference, if any")]
    public string? WebsiteUrl { get; init; } = WebsiteUrl;

    /// <summary>
    /// Whether this Link is used for downloads
    /// </summary>
    [Required]
    [Description("Whether this Link is used for downloads")]
    public bool UseForDownload { get; init; } = UseForDownload;

    public static SourceId<T> From<TEntity>(Schema.SeriesContext.SourceId<TEntity> id) where TEntity : Schema.Identifiable =>
        new(id.Key, id.MangaConnectorName, id.ObjId, id.IdOnConnectorSite, id.WebsiteUrl, id.UseForDownload);
}
