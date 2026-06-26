using System.ComponentModel.DataAnnotations;
using API.Connectors;
using Microsoft.EntityFrameworkCore;

namespace API.Schema.SeriesContext;

[PrimaryKey("Key")]
public class SourceId<T> : Identifiable where T : Identifiable
{
    public T Obj = null!;
    [StringLength(64)] public string ObjId { get; internal set; }

    [StringLength(32)] public string SeriesSourceName { get; private set; }

    [StringLength(256)] public string IdOnConnectorSite { get; init; }
    [Url] [StringLength(512)] public string? WebsiteUrl { get; internal init; }
    public bool UseForDownload { get; internal set; }

    /// <summary>Scan/translation group for this upload, when the connector aggregates several (MangaDex).
    /// Null for direct connectors that host a single version per chapter.</summary>
    [StringLength(64)] public string? ScanGroup { get; internal set; }

    /// <summary>Translated language of this upload (ISO code), when the connector exposes it.</summary>
    [StringLength(16)] public string? Language { get; internal set; }

    public SourceId(T obj, string seriesSourceName, string idOnConnectorSite, string? websiteUrl,
        bool useForDownload = false, string? scanGroup = null, string? language = null)
        : base(BuildKey(obj, seriesSourceName, idOnConnectorSite))
    {
        this.Obj = obj;
        this.ObjId = obj.Key;
        this.SeriesSourceName = seriesSourceName;
        this.IdOnConnectorSite = idOnConnectorSite;
        this.WebsiteUrl = websiteUrl;
        this.UseForDownload = useForDownload;
        this.ScanGroup = scanGroup;
        this.Language = language;
    }

    public SourceId(T obj, SeriesSource seriesSource, string idOnConnectorSite, string? websiteUrl, bool useForDownload = false,
        string? scanGroup = null, string? language = null)
        : this(obj, seriesSource.Name, idOnConnectorSite, websiteUrl, useForDownload, scanGroup, language) { }

    // A chapter source-id is keyed by the chapter it belongs to as well as (connector, id-on-site):
    // comic connectors reuse a bare id across chapters and series (GetComics' "1", IndexerBacked's issue
    // number), so without the chapter those collide on PK_ChapterSourceIds. Series source-ids keep the
    // plain (connector, id-on-site) key — series ids are already unique on a connector, and re-keying
    // them would invalidate the sync-job payloads that store the key.
    private static string BuildKey(T obj, string seriesSourceName, string idOnConnectorSite) =>
        obj is Chapter
            ? TokenGen.CreateToken(typeof(SourceId<T>), seriesSourceName, idOnConnectorSite, obj.Key)
            : TokenGen.CreateToken(typeof(SourceId<T>), seriesSourceName, idOnConnectorSite);

    /// <summary>
    /// EF CORE ONLY!!!
    /// </summary>
    public SourceId(string key, string objId, string seriesSourceName, string idOnConnectorSite, bool useForDownload, string? websiteUrl)
        : base(key)
    {
        this.ObjId = objId;
        this.SeriesSourceName = seriesSourceName;
        this.IdOnConnectorSite = idOnConnectorSite;
        this.WebsiteUrl = websiteUrl;
        this.UseForDownload = useForDownload;
    }

    public override string ToString() => $"{base.ToString()} {Obj}";
}