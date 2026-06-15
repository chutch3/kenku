using System.ComponentModel.DataAnnotations;
using API.Connectors;
using Microsoft.EntityFrameworkCore;

namespace API.Schema.SeriesContext;

[PrimaryKey("Key")]
public class SourceId<T> : Identifiable where T : Identifiable
{
    public T Obj = null!;
    [StringLength(64)] public string ObjId { get; internal set; }

    [StringLength(32)] public string MangaConnectorName { get; private set; }

    [StringLength(256)] public string IdOnConnectorSite { get; init; }
    [Url] [StringLength(512)] public string? WebsiteUrl { get; internal init; }
    public bool UseForDownload { get; internal set; }

    /// <summary>Scan/translation group for this upload, when the connector aggregates several (MangaDex).
    /// Null for direct connectors that host a single version per chapter.</summary>
    [StringLength(64)] public string? ScanGroup { get; internal set; }

    /// <summary>Translated language of this upload (ISO code), when the connector exposes it.</summary>
    [StringLength(16)] public string? Language { get; internal set; }

    public SourceId(T obj, string mangaConnectorName, string idOnConnectorSite, string? websiteUrl,
        bool useForDownload = false, string? scanGroup = null, string? language = null)
        : base(TokenGen.CreateToken(typeof(SourceId<T>), mangaConnectorName, idOnConnectorSite))
    {
        this.Obj = obj;
        this.ObjId = obj.Key;
        this.MangaConnectorName = mangaConnectorName;
        this.IdOnConnectorSite = idOnConnectorSite;
        this.WebsiteUrl = websiteUrl;
        this.UseForDownload = useForDownload;
        this.ScanGroup = scanGroup;
        this.Language = language;
    }

    public SourceId(T obj, SeriesSource seriesSource, string idOnConnectorSite, string? websiteUrl, bool useForDownload = false,
        string? scanGroup = null, string? language = null)
        : this(obj, seriesSource.Name, idOnConnectorSite, websiteUrl, useForDownload, scanGroup, language) { }

    /// <summary>
    /// EF CORE ONLY!!!
    /// </summary>
    public SourceId(string key, string objId, string mangaConnectorName, string idOnConnectorSite, bool useForDownload, string? websiteUrl)
        : base(key)
    {
        this.ObjId = objId;
        this.MangaConnectorName = mangaConnectorName;
        this.IdOnConnectorSite = idOnConnectorSite;
        this.WebsiteUrl = websiteUrl;
        this.UseForDownload = useForDownload;
    }

    public override string ToString() => $"{base.ToString()} {Obj}";
}