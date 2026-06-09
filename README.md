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

It ships as **one Docker image**: the .NET app serves the prerendered Nuxt UI
(from `wwwroot`) and the REST API on the same origin.

| Path   | Part      | Stack                                       |
|--------|-----------|---------------------------------------------|
| `api/` | Backend   | .NET 10 ASP.NET Core REST API + workers     |
| `web/` | Frontend  | Nuxt 4 / Vue 3, prerendered to static assets |

The frontend is a fully-typed client generated at build time from the backend's
OpenAPI spec (`api/API/openapi/API_v2.json`) and bundled into the API image. Because
UI and API share an origin there is no reverse proxy and no CORS hop: the SPA calls
`/v2/...` directly, and `/swagger` serves the API docs.

Published image: `ghcr.io/chutch3/kenku`.

## Quick start

```bash
# Build and run (app + Postgres)
UID=$(id -u) GID=$(id -g) docker compose up --build
```

- Web UI: http://localhost:6531
- API docs: http://localhost:6531/swagger

See [`api/README.md`](api/README.md) and [`web/README.md`](web/README.md) for
component-specific details and configuration.

## Repository layout

```
.
├── api/                 # .NET backend (REST API + background workers)
│   ├── API/             #   application + EF Core contexts + workers
│   └── Tests/           #   xUnit tests
├── web/website/         # Nuxt frontend (prerendered into the API image)
├── Dockerfile           # single image: builds web -> builds api -> bundles both
├── .github/workflows/   # CI: build (image), api-tests, web-tests
└── docker-compose.yaml  # app + Postgres
```

## License & credits

Kenku is licensed under the **GNU GPL v3** — see [`LICENSE`](LICENSE). It builds on
prior GPL-3.0 work; the original authors are credited in [`NOTICE`](NOTICE).
