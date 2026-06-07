namespace API;

/// <summary>When to trigger an external library (Kavita/Komga) rescan relative to downloads.</summary>
public enum LibraryRefreshSetting
{
    /// <summary>Refresh Libraries after all Series are downloaded</summary>
    AfterAllFinished,
    /// <summary>Refresh Libraries after a Series is downloaded</summary>
    AfterMangaFinished,
    /// <summary>Refresh Libraries after every download</summary>
    AfterEveryChapter,
    /// <summary>Refresh Libraries while downloading chapters, every x minutes</summary>
    WhileDownloading
}
