# Database schema

The core series/chapter domain (`SeriesContext`). Kenku splits persistence across several EF Core
contexts, each in its own database — jobs, actions/audit, notifications, libraries, and discovery
live in their own schemas and are omitted here.

```mermaid
erDiagram
    FileLibrary   ||--o{ Series           : holds
    Series        ||--o{ Chapter          : has
    Series        ||--o{ SeriesSourceId   : "found on"
    Chapter       ||--o{ ChapterSourceId  : "found on"
    Series        }o--o{ Author           : "written by"
    Series        }o--o{ SeriesTag        : tagged
    Series        ||--o{ Link             : "external links"
    Series        ||--o{ AltTitle         : "alt titles"
    Series        ||--o| MetadataSource   : "enriched by"
    Series        ||--o{ MetadataEntry    : "fetcher links"
    Series        ||--o{ VolumeMetadata   : "volumes"
    VolumeMetadata||--o{ BundleChapterMap : bundles
    Chapter       ||--o{ BundleChapterMap : "bundled in"

    Series {
        string Key PK
        string Name
        string Description
        string CoverUrl
        enum   ReleaseStatus
        enum   LibraryLayout
        string LibraryId FK
        bool   IsTracked
        uint   Year
        string OriginalLanguage
    }
    Chapter {
        string Key PK
        string ParentMangaId FK
        string ChapterNumber
        int    VolumeNumber
        string Title
        string FileName
        bool   Downloaded
        bool   IsBundled
    }
    SeriesSourceId {
        string Key PK
        string ObjId FK
        string MangaConnectorName
        string IdOnConnectorSite
        string WebsiteUrl
        bool   UseForDownload
        string ScanGroup
        string Language
    }
    ChapterSourceId {
        string Key PK
        string ObjId FK
        string MangaConnectorName
        string IdOnConnectorSite
        bool   UseForDownload
        string ScanGroup
        string Language
    }
    FileLibrary {
        string Key PK
        string BasePath
        string LibraryName
    }
    Author {
        string Key PK
        string AuthorName
    }
    SeriesTag {
        string Tag PK
    }
    Link {
        string Key PK
        string LinkProvider
        string LinkUrl
    }
    AltTitle {
        string Key PK
        string Language
        string Title
    }
    MetadataSource {
        string MangaId PK
    }
    MetadataEntry {
        string MangaId FK
        string MetadataFetcherName
        string Identifier
    }
    VolumeMetadata {
        string MangaId FK
        int    VolumeNumber
        string Title
        string ArchiveFileName
    }
    BundleChapterMap {
        string VolumeKey FK
        string ChapterKey FK
    }
```

> A few tables still carry pre-rename names: `SeriesSourceId` / `ChapterSourceId` are tables
> `MangaConnectorToManga` / `MangaConnectorToChapter`, and `Author`↔`Series` joins through `AuthorToManga`.
> (The main `Series` table was renamed from `Mangas`.) See [`TECHNICAL_DEBT.md`](../TECHNICAL_DEBT.md).
