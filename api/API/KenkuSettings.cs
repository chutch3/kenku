using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace API;

public class KenkuSettings
{
    private static string ComputeDefaultAppData() =>
        Environment.GetEnvironmentVariable("APP_DATA") ??
        (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? (bool.Parse(Environment.GetEnvironmentVariable("DEBUG") ?? "false") ? "./debug" : "/usr/share")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    [JsonIgnore] private readonly object _listLock = new();

    // This property will be saved to and loaded from settings.json
    public string AppData { get; set; } = ComputeDefaultAppData();

    [JsonIgnore] public int Port => int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "6531");
    [JsonIgnore] public bool Debug => bool.Parse(Environment.GetEnvironmentVariable("DEBUG") ?? "false");

    [JsonIgnore] public string WorkingDirectory => Path.Join(AppData, "kenku-api");
    [JsonIgnore] public string SettingsFilePath => Path.Join(WorkingDirectory, "settings.json");
    [JsonIgnore] public string CoverImageCache => Path.Join(WorkingDirectory, "imageCache");
    [JsonIgnore] public string CoverImageCacheOriginal => Path.Join(CoverImageCache, "original");
    [JsonIgnore] public string CoverImageCacheLarge => Path.Join(CoverImageCache, "large");
    [JsonIgnore] public string CoverImageCacheMedium => Path.Join(CoverImageCache, "medium");
    [JsonIgnore] public string CoverImageCacheSmall => Path.Join(CoverImageCache, "small");

    public string DefaultDownloadLocation => Environment.GetEnvironmentVariable("DOWNLOAD_LOCATION") ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "/Series" : Path.Join(Directory.GetCurrentDirectory(), "Series"));
    [JsonIgnore] internal static readonly string DefaultUserAgent = $"Kenku/2.0 ({Enum.GetName(Environment.OSVersion.Platform)}; {(Environment.Is64BitOperatingSystem ? "x64" : "")})";
    public string UserAgent { get; set; } = DefaultUserAgent;
    public int ImageCompression{ get; set; } = 40;
    public bool BlackWhiteImages { get; set; } = false;
    public string FlareSolverrUrl { get; set; } = Environment.GetEnvironmentVariable("FLARESOLVERR_URL") ?? string.Empty;
    /// <summary>
    /// Placeholders:
    /// %M Obj Name
    /// %V Volume
    /// %C Chapter
    /// %T Title
    /// %A Author (first in list)
    /// %I Chapter Internal ID
    /// %i Obj Internal ID
    /// %Y Year (Obj)
    ///
    /// ?_(...) replace _ with a value from above:
    /// Everything inside the braces will only be added if the value of %_ is not null
    /// </summary>
    public string ChapterNamingScheme { get; set; } = "%M - ?V(Vol.%V )Ch.%C?T( - %T)";
    public int WorkCycleTimeoutMs { get; set; } = 20000;

    public string DownloadLanguage { get; set; } = "en";

    public int MaxConcurrentDownloads { get; set; } = (int)Math.Max(Environment.ProcessorCount * 0.75, 1); // Minimum of 1 Tasks, maximum of 0.75 per Core

    public int MaxConcurrentWorkers { get; set; } = Math.Max(Environment.ProcessorCount, 4); // Minimum of 4 Tasks, maximum of 1 per Core

    public VolumeResolutionStrategy VolumeResolutionStrategy { get; set; } = VolumeResolutionStrategy.ExactThenGuess;

    public int VolumeResolutionParallelism { get; set; } = 3;

    public LibraryRefreshSetting LibraryRefreshSetting { get; set; } = LibraryRefreshSetting.AfterMangaFinished;

    public int RefreshLibraryWhileDownloadingEveryMinutes { get; set; } = 10;

    /// <summary>
    /// Names of <see cref="API.MangaConnectors.SeriesSource"/> instances that have been explicitly disabled.
    /// Connectors absent from this set are considered enabled (opt-out model).
    /// </summary>
    public HashSet<string> DisabledConnectors { get; set; } = [];

    /// <summary>
    /// Origins permitted for browser CORS requests. Empty (the default) preserves the historical
    /// "allow any origin" behaviour; populate it to restrict the API to specific frontends.
    /// </summary>
    public string[] CorsAllowedOrigins { get; set; } = [];

    /// <summary>True when no explicit origins are configured, i.e. any origin is allowed.</summary>
    [JsonIgnore] public bool CorsAllowAnyOrigin => CorsAllowedOrigins.Length == 0;

    // ---------- Indexer (Prowlarr sync target) ----------
    // Indexers follow the *arr model: a list of Torznab/Newznab endpoints. You can ADD them by hand
    // (ManualIndexers) and/or let Prowlarr PUSH them in (SyncedIndexers below). Prowlarr is configured
    // to point at Kenku as a Mylar application and syncs the matching indexers into SyncedIndexers.

    /// <summary>Manually-configured Torznab/Newznab indexers.</summary>
    public List<API.Indexers.ManualIndexerConfig> ManualIndexers { get; set; } = [];

    /// <summary>Indexers Prowlarr has pushed/synced into Kenku (source of truth lives in Prowlarr).</summary>
    public List<SyncedIndexerConfig> SyncedIndexers { get; set; } = [];

    /// <summary>
    /// Fallback comic category IDs for indexer searches, used ONLY for indexers that carry no
    /// categories of their own — an indexer's Prowlarr-synced / manually-configured categories take
    /// precedence (see <see cref="API.Indexers.TorznabIndexer"/>). Default 8000 is the Newznab
    /// "Books" parent (the comics subcategory is 8030, and many trackers use their own IDs), so this
    /// is only a coarse last resort.
    /// </summary>
    public int[] IndexerComicCategories { get; set; } = [8000];

    /// <summary>API key Prowlarr uses to authenticate against Kenku's Mylar-emulating application endpoint.</summary>
    public string ApiKey { get; set; } = GenerateApiKey();

    /// <summary>128 bits of cryptographically-strong randomness as lowercase hex (a GUID is not designed as a secret).</summary>
    private static string GenerateApiKey() =>
        System.Security.Cryptography.RandomNumberGenerator.GetHexString(32, lowercase: true);

    /// <summary>True when at least one indexer source (manual or Prowlarr-synced) is configured.</summary>
    [JsonIgnore] public bool IndexerConfigured => ManualIndexers.Count > 0 || SyncedIndexers.Count > 0;

    // ---------- Download clients (qBittorrent, ...) ----------
    /// <summary>User-configurable download clients. The lowest-Priority enabled client is used.</summary>
    public List<DownloadClientConfig> DownloadClients { get; set; } = [];

    /// <summary>Directory the torrent client downloads into; the completion worker moves files out of here.</summary>
    [JsonIgnore] public string TorrentStagingDirectory => Path.Join(WorkingDirectory, "torrent-staging");

    [JsonIgnore] public bool AnyDownloadClientConfigured => DownloadClients.Any(c => c.Enabled);

    // ---------- Metron metadata (metron.cloud) ----------
    /// <summary>Metron account username (HTTP Basic auth). Empty disables Metron lookups.</summary>
    public string MetronUsername { get; set; } = "";
    /// <summary>Metron account password (HTTP Basic auth). Persisted, but never returned by the API.</summary>
    public string MetronPassword { get; set; } = "";

    // ---------- Release selection (v1: simple scoring) ----------
    public int ReleaseMinSeeders { get; set; } = 2;
    public string[] ReleasePreferredTokens { get; set; } = ["cbz"];
    public string[] ReleaseBlockedTokens { get; set; } = ["cbr", "pdf"];

    public KenkuSettings()
    {
        // WorkingDirectory is created by the AppData setter when AppData is overridden
        // (e.g. via object initializer). For the default path it is created in Save().
    }

    public static KenkuSettings Load()
    {
        // 1. Use the "Safety Net" logic to find where the file SHOULD be
        string discoveryPath = Path.Join(ComputeDefaultAppData(), "kenku-api", "settings.json");

        if (!File.Exists(discoveryPath))
        {
            var defaults = new KenkuSettings();
            // Defaults already has AppData set to _defaultAppData
            defaults.Save();
            return defaults;
        }

        // 2. Load the file. If the file has a different "AppData" inside it,
        // the json deserializer will overwrite the default value.
        var json = File.ReadAllText(discoveryPath);
        return JsonConvert.DeserializeObject<KenkuSettings>(json, new StringEnumConverter())
               ?? new KenkuSettings();
    }

    public void Save()
    {
        lock (this) // Good practice now that it's a shared reference in DI
        lock (_listLock) // Guard SyncedIndexers/DownloadClients against mutation while serializing
        {
            Directory.CreateDirectory(WorkingDirectory);
            File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(this, Formatting.Indented, new StringEnumConverter()));
        }
    }

    public void SetUserAgent(string value)
    {
        this.UserAgent = value;
        Save();
    }

    public void UpdateImageCompression(int value)
    {
        this.ImageCompression = value;
        Save();
    }

    public void SetBlackWhiteImageEnabled(bool enabled)
    {
        this.BlackWhiteImages = enabled;
        Save();
    }

    public void SetChapterNamingScheme(string scheme)
    {
        this.ChapterNamingScheme = scheme;
        Save();
    }

    public void SetFlareSolverrUrl(string url)
    {
        this.FlareSolverrUrl = url;
        Save();
    }

    public void SetMetronCredentials(string username, string password)
    {
        this.MetronUsername = username;
        this.MetronPassword = password;
        Save();
    }



    public void RegenerateApiKey()
    {
        ApiKey = GenerateApiKey();
        Save();
    }

    public void AddOrUpdateSyncedIndexer(SyncedIndexerConfig config)
    {
        lock (_listLock)
        {
            int idx = SyncedIndexers.FindIndex(i =>
                string.Equals(i.Name, config.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.Protocol, config.Protocol, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                SyncedIndexers[idx] = config with { Id = SyncedIndexers[idx].Id };
            else
                SyncedIndexers.Add(config with { Id = NextId(SyncedIndexers.Select(x => x.Id)) });
        }
        Save();
    }

    public void RemoveSyncedIndexer(string name, string protocol)
    {
        lock (_listLock)
            SyncedIndexers.RemoveAll(i =>
                string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.Protocol, protocol, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    /// <summary>Thread-safe snapshot of the synced indexers for concurrent reads.</summary>
    public SyncedIndexerConfig[] SnapshotSyncedIndexers()
    {
        lock (_listLock)
            return SyncedIndexers.ToArray();
    }

    public int AddDownloadClient(DownloadClientConfig config)
    {
        int id;
        lock (_listLock)
        {
            id = NextId(DownloadClients.Select(c => c.Id));
            DownloadClients.Add(config with { Id = id });
        }
        Save();
        return id;
    }

    public bool UpdateDownloadClient(DownloadClientConfig config)
    {
        bool updated;
        lock (_listLock)
        {
            int idx = DownloadClients.FindIndex(c => c.Id == config.Id);
            updated = idx >= 0;
            if (updated)
            {
                // The password is redacted on the API read path, so an edit submitted by the UI may
                // carry a blank password meaning "unchanged" rather than "clear it". Preserve the
                // stored secret unless a new non-blank value was supplied.
                string? password = string.IsNullOrEmpty(config.Password)
                    ? DownloadClients[idx].Password
                    : config.Password;
                DownloadClients[idx] = config with { Password = password };
            }
        }
        if (updated)
            Save();
        return updated;
    }

    public bool RemoveDownloadClient(int id)
    {
        int removed;
        lock (_listLock)
            removed = DownloadClients.RemoveAll(c => c.Id == id);
        if (removed > 0)
            Save();
        return removed > 0;
    }

    /// <summary>Thread-safe snapshot of the download clients for concurrent reads.</summary>
    public DownloadClientConfig[] SnapshotDownloadClients()
    {
        lock (_listLock)
            return DownloadClients.ToArray();
    }

    private static int NextId(IEnumerable<int> ids)
    {
        int max = 0;
        foreach (int id in ids)
            if (id > max) max = id;
        return max + 1;
    }

    public void SetDownloadLanguage(string language)
    {
        this.DownloadLanguage = language;
        Save();
    }

    public void SetMaxConcurrentDownloads(int value)
    {
        this.MaxConcurrentDownloads = value;
        Save();
    }

    public void SetMaxConcurrentWorkers(int value)
    {
        this.MaxConcurrentWorkers = value;
        Save();
    }

    public void SetLibraryRefreshSetting(LibraryRefreshSetting setting)
    {
        this.LibraryRefreshSetting = setting;
        Save();
    }

    public void SetRefreshLibraryWhileDownloadingEveryMinutes(int value)
    {
        this.RefreshLibraryWhileDownloadingEveryMinutes = value;
        Save();
    }

    /// <summary>
    /// Persists the enabled/disabled state for a connector by name.
    /// </summary>
    public void SetConnectorEnabled(string connectorName, bool enabled)
    {
        if (enabled)
            DisabledConnectors.Remove(connectorName);
        else
            DisabledConnectors.Add(connectorName);
        Save();
    }

    /// <summary>
    /// Applies the persisted <see cref="DisabledConnectors"/> set to a collection of connector instances.
    /// Call this once at startup after the DI container is built.
    /// </summary>
    public void ApplyDisabledConnectors(IEnumerable<MangaConnectors.SeriesSource> connectors)
    {
        foreach (var connector in connectors)
            connector.Enabled = !DisabledConnectors.Contains(connector.Name);
    }
}

// These are the persistence models (written to settings.json by Newtonsoft). They carry secrets
// (ApiKey/Password); the API never returns them directly — SettingsController projects a secret-free
// response DTO instead. Keep secrets OUT of any type returned from a controller.
public record SyncedIndexerConfig(
    int Id,
    string Name,
    string Url,
    string ApiKey,
    int[] Categories,
    string Protocol,
    bool Enabled);

public record DownloadClientConfig(
    int Id,
    string Name,
    DownloadClientType Type,
    string BaseUrl,
    string? Username,
    string? Password,
    string? Category,
    bool Enabled,
    int Priority);

public enum DownloadClientType
{
    QBittorrent
}
