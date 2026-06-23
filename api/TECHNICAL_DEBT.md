# Kenku technical debt

Known items deliberately deferred. Each has been investigated and a concrete recipe is recorded
below so the next person picking it up doesn't repeat the discovery.

---

## Legacy column / identifier names

The `Mangas` table was renamed to `Series` (migration `RenameMangasTableToSeries`). EF Core 10 generates
a non-destructive `RenameTable` (+ index/PK/FK constraint renames) for an entity-type rename — the older
`DropTable + CreateTable` behaviour no longer applies — so no hand-written SQL was needed. Verified Up and
Down against a Postgres DB seeded with rows; data is preserved both directions. The dead `[Table("MangaConnector")]`
attribute on `SeriesSource` was also removed (it mapped to no real table — `SeriesSource` is a DI service,
not an entity).

The **remaining** legacy names are *column / property* names, and these are **not** a pure DB rename — they
are serialized in the public API and used as route tokens, so renaming is a cross-cutting change:

- `Chapter.ParentMangaId`, `MetadataEntries.MangaId`, `MetadataSources.MangaId`, `VolumeMetadata.MangaId`,
  `NotificationConnector.MangaConnectorName` — each is a C# property **and** a serialized DTO field
  (e.g. `Chapter.parentMangaId`), 25–35 references apiece. Renaming requires, in lock-step: a `RenameColumn`
  migration **+** the property rename **+** the OpenAPI/contract change **+** the matching frontend updates.
- The website's URL parameter `[mangaId]` (folder name) and `MangaId` (route token) follow from the same
  names. Code-internal otherwise; harmless.

Worth doing as a dedicated, coordinated pass — not bundled with unrelated work.

---

## Frontend: remaining config-file-only settings

- **Secrets in `GET /v2/Settings`** — passwords/API keys are serialised in the settings GET (matches
  the pre-existing pattern; the API has no auth layer anyway). Modals never pre-fill them. If an auth
  layer is added later, redact these via a response DTO.
