<div align="center">
  <h1>Kenku</h1>
  <p><em>Self-hosted manga &amp; comic downloader with metadata enrichment — API + web UI in one repo.</em></p>

  ![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)
</div>

## About

Kenku automatically downloads chapters and metadata from scanlation sites and
indexers, packages them as `.cbz` archives, enriches them with metadata, and can
trigger library scans in [Komga](https://komga.org/) and
[Kavita](https://www.kavitareader.com/) plus push notifications via Gotify, Ntfy,
Pushover and generic webhooks.

It is a single repository containing two deployable services:

| Path    | Service   | Stack                                   | Image                              |
|---------|-----------|-----------------------------------------|------------------------------------|
| `api/`  | Backend   | .NET 10 ASP.NET Core REST API + workers | `ghcr.io/chutch3/kenku-api`        |
| `web/`  | Frontend  | Nuxt 4 / Vue 3 static SPA behind nginx  | `ghcr.io/chutch3/kenku-web`        |

The backend exposes a versioned REST API (`/v2`, OpenAPI at `/swagger`). The
frontend is a thin, fully-typed client generated from that OpenAPI spec; nginx
reverse-proxies `/api`, `/v2` and `/swagger` to the backend.

## Quick start

```bash
# Build and run the full stack (API + web + Postgres)
UID=$(id -u) GID=$(id -g) docker compose up --build
```

- Web UI: http://localhost:9555
- API + Swagger: http://localhost:6531/swagger

See [`api/README.md`](api/README.md) and [`web/README.md`](web/README.md) for
component-specific details and configuration.

## Repository layout

```
.
├── api/                 # .NET backend (REST API + background workers)
│   ├── API/             #   application + EF Core contexts + workers
│   └── Tests/           #   xUnit tests
├── web/                 # Nuxt frontend
│   ├── website/         #   Nuxt app
│   └── nginx/           #   reverse-proxy config
├── .github/workflows/   # CI: build-api, build-web, run-tests
└── docker-compose.yaml  # full-stack compose
```

## Relationship to Tranga (attribution)

Kenku began as a fork of [**Tranga**](https://github.com/C9Glax/tranga) and its
companion [**tranga-website**](https://github.com/C9Glax/tranga-website) by
[C9Glax](https://github.com/C9Glax) and contributors. Since then it has diverged
substantially — the two projects have been merged into this monorepo and the
architecture has been reworked (indexer/torrent acquisition, Metron metadata,
restructured settings, and more). Because of that divergence it is maintained as
a distinct project under a new name rather than as a drop-in continuation of the
original.

Huge thanks to the original authors. See [`NOTICE`](NOTICE) for attribution
details. Kenku remains licensed under the **GNU GPL v3** — see [`LICENSE`](LICENSE).
