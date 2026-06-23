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
2. Remove the `[Table("MangaConnector")]` attribute from `API/Connectors/SeriesSource.cs`.
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

## Frontend: remaining config-file-only settings

- **Manual (non-Prowlarr) indexers** (`ManualIndexers`) — no add/remove UI yet; only Prowlarr-synced
  indexers are reachable from the website (`IndexersCard` is read-only). Add a small list editor if
  standalone Torznab feeds are needed.
- **Secrets in `GET /v2/Settings`** — passwords/API keys are serialised in the settings GET (matches
  the pre-existing pattern; the API has no auth layer anyway). Modals never pre-fill them. If an auth
  layer is added later, redact these via a response DTO.


## Legacy column / identifier names

- `MetadataEntries.MangaId`, `MetadataSources.MangaId`, `VolumeMetadata.MangaId`,
  `Chapter.ParentMangaId`, `NotificationConnector.MangaConnectorName` (parameter names) — kept as
  legacy column names in the DB. Renaming any of these requires a coordinated migration with the
  same care as the table rename above.
- The website's URL parameter `[mangaId]` (folder name) and `MangaId` (route token) still use the
  legacy name. Code-internal only; harmless. Rename only if doing the API parameter rename in
  lock-step.
