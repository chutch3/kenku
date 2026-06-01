# Kenku technical debt

Known items deliberately deferred. Each has been investigated and a concrete recipe is recorded
below so the next person picking it up doesn't repeat the discovery.

---

## DB rename migration (Mangas → Series, MangaConnector → SeriesSource)

**Status.** The C# entity types were renamed during the `Manga`→`Series` rename. The DB still has
the old table names. `[Table("Mangas")]` on `Series` and `[Table("MangaConnector")]` on
`SeriesSource` preserve the mapping at runtime; no data migration has been applied.

**Why not auto-generated.** `dotnet ef migrations add` does **not** recognise C# entity-type renames
as table renames — it sees a new entity type with a different table name and produces
`DropTable("Mangas") + CreateTable("Series")`, which destroys all data. Tested twice during the
`Manga`→`Series` rollout; both times the auto-generated migration was data-destructive and was
discarded. Do not run `dotnet ef migrations add` for this rename without rewriting the result.

**Recipe to finish.** On a branch, against a **non-production dev DB with a snapshot you can
restore**:

1. Remove the `[Table("Mangas")]` attribute from `API/Schema/SeriesContext/Series.cs`.
2. Remove the `[Table("MangaConnector")]` attribute from `API/MangaConnectors/SeriesSource.cs`.
3. In `API/Schema/SeriesContext/SeriesContext.cs`, revert the join-table name in
   `UsingEntity("SeriesTagToSeries", …)` back to `UsingEntity("MangaTagToManga", …)` — keeping the
   join table name pinned avoids EF wanting to drop+recreate it. (Or commit to renaming it too and
   add the corresponding `RenameTable` op.)
4. Run `dotnet ef migrations add RenameMangaTablesToSeries --project API --context SeriesContext --output-dir Migrations/Manga`.
5. **Open the generated `.cs` file and rewrite the Up() / Down() methods by hand**:
   - Replace `DropTable("Mangas") + CreateTable("Series")` with a single
     `migrationBuilder.RenameTable(name: "Mangas", newName: "Series");`
   - Keep the `RenameColumn` ops EF generates for shadow FKs (`AltTitle.MangaKey` → `SeriesKey`,
     `Link.MangaKey` → `SeriesKey`) — those are correct as-is.
   - Keep the `RenameIndex` ops EF generates.
   - For each `FK_*_Mangas_*` constraint, add a `migrationBuilder.Sql(...)` block to rename it via
     `ALTER TABLE "<table>" RENAME CONSTRAINT "FK_<old>" TO "FK_<new>"`. EF Core's MigrationBuilder
     does not have a `RenameConstraint` helper for Postgres.
   - Add: `migrationBuilder.Sql("""ALTER TABLE "Series" RENAME CONSTRAINT "PK_Mangas" TO "PK_Series" """);`
   - Same pattern for the `MangaConnector` table → `SeriesSource` rename if you're also doing it.
   - Write the reverse operations in `Down()`.
6. Run the migration against the dev DB. Verify with `\d Series`, `\d Chapters` (its FK should now
   point to "Series"), and a `SELECT COUNT(*) FROM Series` matching the previous count from "Mangas".
7. Re-run the full test suite. The `OnModelCreating` config for `MetadataEntry.Series` already has
   `[ForeignKey(nameof(MangaId))]` to prevent EF inferring a shadow `SeriesKey` column — keep it.
8. Commit the migration + the model changes together. The `[Table]` attributes can be removed in
   the same commit since the DB now matches.

**Why this is risky.** Renaming tables in Postgres is fast and safe (metadata-only), but any
syntactic mistake in the hand-written migration runs straight against production data. Don't skip
the dev-DB verification step.

---

## Concrete `Kind = Torrent` `SeriesSource`

**Status.** All torrent infrastructure exists and no concrete `SeriesSource` declares
`Kind = AcquisitionKind.Torrent` yet, so the torrent path is built+tested but dormant in production.

**Indexer model (important — not coupled to Prowlarr).** An indexer is a Torznab/Newznab endpoint
(`IIndexer` / `TorznabIndexer`). Indexers come from `IIndexerProvider`s:
`ConfiguredIndexerProvider` (manually-added, from `settings.ManualIndexers`) and
`SyncedIndexerProvider` (exposes the indexers Prowlarr has *pushed/synced* into
`settings.SyncedIndexers`, reading them live on every search so updates need no restart).
`AggregateIndexerSearch : IIndexerClient` fans out across all of them. This mirrors the
*arr model: you add indexers by hand or let Prowlarr sync them in; Prowlarr is one source of indexers,
not the indexer. A concrete torrent `SeriesSource` therefore depends on `IIndexerClient` (the
aggregate) and never on Prowlarr directly.

**Suggested implementation: `IndexerBackedSeriesSource` (name it for the model, not for Prowlarr).**

- Override `Kind => AcquisitionKind.Torrent`.
- `SearchManga(query)`: call `IIndexerClient.Search` with the user's query, dedupe results by parsed
  series name (strip issue numbers, year, tags from the release title), return one `Series` per
  distinct match. Cover/description metadata will be sparse — pair with a metadata fetcher (Metron).
- `GetMangaFromId(id)`: round-trip metadata for the stored series identifier.
- `GetChapters(seriesId, language)`: call `IIndexerClient.Search` again with just the series name,
  parse issue numbers from release titles into distinct `Chapter` rows. Title parsing is the hardest
  bit; a regex over common comic release patterns (`Series Title 060 (2024)`, `Series Title #60`,
  etc.) is a reasonable v1.
- `GetChapterImageUrls` / `DownloadImage`: throw (not used; `Kind=Torrent` bypasses these).

Estimated effort: 1-2 hours including parser tests.

**Prowlarr push-sync (DONE).** Kenku now emulates a Mylar application: Prowlarr is configured to point
at Kenku (base URL + API key) and *pushes* indexer definitions into Kenku via the Mylar-emulating `/api`
endpoint (`MylarApiController`, `cmd=getVersion|listProviders|addProvider|changeProvider|delProvider`,
authenticated by the `apikey` query parameter). They persist in `settings.SyncedIndexers` and take effect
live. Per-indexer enable/disable is honoured by `SyncedIndexerProvider` (only enabled configs are searched).

---

## Frontend: settings UI for new integrations

DONE: the Settings page has a read-only Prowlarr-setup panel (Kenku base URL + API key with copy and a
regenerate button, plus a hint to add Kenku as a *Mylar* application in Prowlarr), a list of the
Prowlarr-synced indexers, download-client management (add/edit/remove via `DownloadClientModal`, backed
by `GET/POST/PUT/DELETE /v2/Settings/DownloadClients` and `GET /v2/Settings/ApiKey` +
`POST /v2/Settings/ApiKey/Regenerate`), and Metron credentials. Metron also appears automatically in the
existing metadata-fetcher table.

STILL config-file-only:
- **Manual (non-Prowlarr) indexers** (`ManualIndexers`) — no add/remove UI yet; only Prowlarr-synced
  indexers are reachable from the website. Add a small list editor if standalone Torznab feeds are
  needed.
- **Pending torrent downloads view** — no UI surfaces in-flight torrents (the `TorrentCompletionWorker`
  state). A read-only panel would be nice-to-have.
- **Secrets in `GET /v2/Settings`** — passwords/API keys are serialised in the settings GET (matches
  the pre-existing pattern; the API has no auth layer anyway). Modals never pre-fill them. If an auth
  layer is added later, redact these via a response DTO.


## Other items

- `MetadataEntries.MangaId`, `MetadataSources.MangaId`, `VolumeMetadata.MangaId`,
  `Chapter.ParentMangaId`, `NotificationConnector.MangaConnectorName` (parameter names) — kept as
  legacy column names in the DB. Renaming any of these requires a coordinated migration with the
  same care as the table rename above.
- The website's URL parameter `[mangaId]` (folder name) and `MangaId` (route token) still use the
  legacy name. Code-internal only; harmless. Rename only if doing the API parameter rename in
  lock-step.
