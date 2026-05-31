<div align="center">
  <h1>Kenku — Backend (API)</h1>
  <p><em>.NET 10 REST API + background workers that download manga/comic chapters and metadata.</em></p>

  ![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)
</div>

This is the backend half of the [Kenku](../README.md) monorepo. It is a REST API and
a background job runner in one process: REST endpoints configure and start workers
(search a series, fetch chapters, download, organize), and the workers do the work.

In production it is bundled into the single Kenku image and served alongside the web
UI on the same origin (see the root [`README.md`](../README.md)). This document covers
the backend itself — its capabilities, configuration, and local development.

## What it does

- Downloads chapters from scanlation sites and indexers, packaging each chapter as a
  `.cbz` archive (optionally with `ComicInfo.xml`, JPEG compression, or grayscale to
  save space).
- **Sources**: MangaDex, MangaWorld, AsuraComic, WeebCentral, plus indexer-backed
  torrent acquisition (Torznab / Prowlarr → qBittorrent).
- **Metadata enrichment**: MyAnimeList (via Jikan) and Metron.
- **Library integration**: triggers scans in [Komga](https://komga.org/) and
  [Kavita](https://www.kavitareader.com/).
- **Notifications**: Gotify, Ntfy, Pushover, and generic REST webhooks.
- Persists everything in PostgreSQL via EF Core (series, chapters, libraries,
  notifications, and an action/audit log).

## API

- Versioned REST API under `/v2`.
- Interactive docs (Swagger UI) at `/swagger`.
- The OpenAPI specification is generated to [`API/openapi/API_v2.json`](API/openapi/API_v2.json)
  on build; the frontend's typed client is generated from it.

## Configuration

The container is configured through environment variables:

| Environment Variable              | Default          | Description                                                                 |
|-----------------------------------|------------------|-----------------------------------------------------------------------------|
| `PORT`                            | `6531`           | Port the API listens on.                                                     |
| `POSTGRES_HOST`                   | `kenku-pg:5432`  | Host address of the PostgreSQL database.                                     |
| `POSTGRES_DB`                     | `postgres`       | Database name.                                                               |
| `POSTGRES_USER`                   | `postgres`       | Database username.                                                           |
| `POSTGRES_PASSWORD`               | `postgres`       | Database password.                                                           |
| `DOWNLOAD_LOCATION`               | `/Manga`         | Download target directory (path inside the container).                       |
| `FLARESOLVERR_URL`                | _(empty)_        | URL of a FlareSolverr instance, for Cloudflare-protected sources.           |
| `POSTGRES_COMMAND_TIMEOUT`        | `60`             | Postgres command timeout (seconds).                                          |
| `POSTGRES_CONNECTION_TIMEOUT`     | `30`             | Postgres connection timeout (seconds).                                       |
| `CHECK_CHAPTERS_BEFORE_START`     | `true`           | Reconcile the "downloaded" state against disk on startup (can be slow).      |
| `MATCH_EXACT_CHAPTER_NAME`        | `true`           | Match the stored filename exactly with the file on disk.                     |
| `CREATE_COMICINFO_XML`            | `true`           | Include `ComicInfo.xml` in `.cbz` archives.                                  |
| `ALWAYS_INCLUDE_VOLUME_IN_FILENAME` | `false`        | Always include a volume in filenames (default `Vol. 0`).                     |
| `HTTP_REQUEST_TIMEOUT`            | `10`             | Per-request timeout for source connectors (seconds).                        |
| `REQUESTS_PER_MINUTE`             | `90`             | Per-host rate limit for source connectors.                                  |
| `MINUTES_BETWEEN_NOTIFICATIONS`   | `1`              | Interval at which queued notifications are sent.                            |
| `HOURS_BETWEEN_NEW_CHAPTERS_CHECK`| `3`              | Interval at which sources are polled for new chapters.                      |
| `WORKER_TIMEOUT`                  | `600`            | Seconds a worker may run before it is forcefully cancelled.                 |

Per-user settings (download language, naming scheme, connector/indexer/torrent
credentials, etc.) are stored in the app's data directory, which `docker-compose.yaml`
persists via the `settings` volume.

## Local development

Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
and a reachable PostgreSQL instance.

```bash
cd api
dotnet restore                      # resolves the solution in this directory
dotnet run --project API            # serves the API on :6531
dotnet test Tests/Tests.csproj      # run the unit tests
```

## Built with

- [ASP.NET Core](https://dotnet.microsoft.com/en-us/apps/aspnet) + [EF Core](https://learn.microsoft.com/en-us/ef/core/)
- [PostgreSQL](https://www.postgresql.org/) via [Npgsql](https://github.com/npgsql/npgsql)
- [Swashbuckle / Swagger](https://github.com/domaindrivendev/Swashbuckle.AspNetCore)
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp), [HtmlAgilityPack](https://github.com/zzzprojects/html-agility-pack), [PuppeteerSharp](https://www.puppeteersharp.com/) + [Chromium](https://www.chromium.org/)
- [Jikan.Net](https://github.com/Ervie/jikan.net), [log4net](https://logging.apache.org/log4net/), [xUnit](https://xunit.net/)

## Contributing

See [CONTRIBUTING](CONTRIBUTING.md).

## License

GNU GPL v3 — see [`LICENSE`](../LICENSE). Credits in [`NOTICE`](../NOTICE).
