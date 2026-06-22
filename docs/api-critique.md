# API implementation critique — 2026-06-09 (post v0.13.0)

Scope: `api/API` + `api/Tests`, judged against the development-context prompt (outside-in TDD, Circle
architecture, mocking rules, C# structure/DI rules) and against the repo's own working rules
(`docs/job-runtime-rearch.md` §7). Verdict up front: **test discipline and module layout are strong;
DI registration and controller-layer duplication are the two real debts.** Everything below has
file:line evidence.

---

## 1. Dependency injection — the weakest area

**Services are hand-built, not container-built.** 13 of the 14 classes in `API/Services/` are never
registered; every job handler news them up while manually pulling their dependencies out of the scope:

- `DownloadChapterHandler.cs:38-45` — `new ChapterDownloadService(...)` assembling **seven** deps by hand
  (including `new LibraryLayoutResolver()`, which *has* an interface that's ignored here).
- Same pattern ×10: `SyncSeriesChaptersHandler.cs:34`, `DownloadCoverHandler.cs:34`,
  `ReconcileVolumeBundleHandler.cs:46`, `PlaceChapterFileHandler.cs:36`, `CleanupHandler.cs:35`,
  `MoveDataHandler.cs:33`, `FinalizeTorrentHandler.cs:37`, `VerifyDownloadStateHandler.cs:25`,
  `RefreshExternalMetadataHandler.cs:33`.
- `VolumeResolutionService` is the **one** service done right (`AddScoped`, Program.cs:168, resolved in
  its handler) — the correct pattern exists in-repo and is followed once.

This contradicts both the context ("Define interfaces for all dependencies… enable mocking") and the
repo's own rule (§7: "DI is the rule — never hand-construct what the container should build"). The
practical cost is real: every new service dependency means editing a handler's hand-wiring (we did it
twice today), and constructor changes ripple as compile errors through handlers instead of being
absorbed by the container.

**Program.cs is not clean.** 356 lines: ~41 inline registrations (connectors, fetchers, acquirers, 11
handlers, 10 hosted reconcilers, 5 DbContexts) **plus** migrations, seeding, a startup notification
with a random emoji, and connector-state application (lines 273-349). The context's rule is "register
services via static layer extension methods"; only `AddTorrentAcquisitionPath` exists
(`Extensions/ServiceCollectionExtensions.cs`). This is also where the known `RunStartup` coupling smell
lives — reconcilers are gated on the same flag that gates migrate+seed.

**Lifetimes are sound, though.** Scoped contexts/`IJobStore`/`Dispatcher`; singleton handlers that
deliberately open their own scope per job (documented, prevents the #31-era shared-context failures);
no captive dependencies found; `ReleaseSelector`/`TorrentAcquirer` transient so settings PATCHes apply
live. The weak spot is `KenkuSettings`: a mutable singleton that is simultaneously config, persistence
(`Save()` writes settings.json under two locks), and API surface — a 12-factor wobble and a growing
god-object (its `Set*` method list grows with every settings endpoint).

**Injection style drifts.** Constructor injection for contexts/settings, `[FromServices] IJobStore/IClock`
peppered across 17 actions in 5 controllers. There's a defensible logic (only job-enqueueing actions
need them) but it's applied inconsistently and adds noise; ctor injection would be simpler and uniform.

**Interfaces: deliberate deviation, mostly fine.** Only 5 of ~18 services have interfaces. That breaks
the letter of the context's "interfaces for all dependencies", but the tests never need to mock these
services (they test real instances with stubbed *edges*), so blanket interfaces would be speculative
abstraction. Keep the deviation; write it down as policy: **interfaces at seams that get stubbed,
concrete classes inside the inner ring.**

## 2. Duplication — three concrete offenders

**(a) The cover+sync enqueue pair — 5 near-identical ~10-line blocks**:
`SearchController.cs:171-183`, `SeriesController.cs:306-315` (MarkAsRequested), `:371-381`
(ChangeLibrary), `:419-427` (Rematch), `:486-495` (SyncNow), plus cover-only in `Kenku.cs:114-118`.
Each repeats the Job ctor with the resourceKey/dedupKey conventions that must stay aligned by hand.
Two of those sites were added *today following the local idiom* — the idiom is the problem.
→ Extract one helper (e.g. `SeriesJobs.EnqueueCoverAndSync(jobStore, clock, sourceId, language)`);
the dedup/resourceKey conventions then live in exactly one place.

**(b) DTO mapping — 10+ hand-rolled copies.** `new DTOs.SourceId<…>(id.Key, id.MangaConnectorName,
id.ObjId, id.IdOnConnectorSite, …)` appears in SearchController ×3, SeriesController ×5,
ChaptersController ×4; `MinimalSeries`/`Series` mappings similarly. Only `QueuedJob` has a
`From()` factory. Worse, the mapping hides a live trap: **DTO field names are inverted relative to
the schema** — DTO `ForeignKey` carries the *series key*, DTO `ObjId` carries the *site id*
(`DTOs/SourceId.cs:10` vs `Schema/.../SourceId.cs`). This cost us a real debugging detour today and
will cost the next person too.
→ `From(entity)` factories on the three DTOs; rename `ForeignKey`→`SeriesKey` / `ObjId`→`IdOnConnectorSite`
in a coordinated frontend-regen commit (the openapi codegen makes this cheap).

**(c) The reconciler skeleton ×11.** Every hosted reconciler repeats the identical `ExecuteAsync`:
RunStartup gate → loop → scope → static `ScanAndEnqueueAsync` → catch/log → `Task.Delay(Interval)`
(e.g. `VolumeBundleReconciler.cs:28-47`, `DownloadReconciler.cs:30-52`; 11 copies, only interval and
scan deps differ).
→ One abstract `Reconciler` base (interval + `Scan(scope, ct)`) deletes ~150 lines, centralizes the
RunStartup gate (the place to fix the coupling smell once), and makes new reconcilers a 15-line file.
The static `ScanAndEnqueueAsync` seam should survive — it's what keeps them unit-testable.

## 3. Class structure

**SeriesController is a god controller**: 662 lines, ~8 endpoint groups (listing, rollup, covers,
download toggles, library moves with file-move enqueues, merge, rematch, recheck). Two specific
misplacements: `ChangeLibrary` contains a connector-import + library-move + job-enqueue workflow
inline, and `GetSeriesRollup` runs a three-context aggregation (Series+Jobs+Actions) in the action
body. Both are inner-ring logic that belongs in services with the controller as a thin adapter — the
rollup especially, since today its only coverage is the booted-app test (fine as a net, but there's no
unit seam for its edge cases).

**Entity/service hybrids straddle the ring boundary.** `MetadataFetcher` subclasses (MyAnimeList,
Metron) are EF-materialized rows *and* DI singletons holding HTTP clients, with `internal` "EF ONLY!!!"
constructors as the tell. `SeriesSource` likewise: the abstract base is an entity-referenced identity
*and* ~150 lines of HTTP + ImageSharp cover pipeline (`SeriesSource.cs:85-140`), with a nullable-by-
default `downloadClient` that produced exactly the NullRef class we fixed for the indexer source
today. The Circle architecture wants those outer-ring concerns (HTTP, image processing) behind owned
services the entities don't carry.

**What's genuinely good:** the JobRuntime. Small uniform handlers (~40 lines, one shape), `Job` as a
real unit of work, `IJobStore` with two implementations sharing `JobReadySelection` so the DF tests pin
both, injectable clock/backoff/caps. This is the cleanest module in the codebase and the rearch shows.

## 4. Module structure — conforms

Folders ↔ namespaces match 100% on spot-checks; interfaces sit in per-group `Interfaces/` folders
consistently; tests mirror sources 1:1 (`Tests/Unit/<folder>` for every source folder except
`Notifications/` — its tests live under JobRuntime/Schema — and `Extensions/`, untested). Integration
tests are all in `Tests/Integration` on the booted-app pattern; nothing integration-shaped hides under
Unit/. Two nits: metadata fetchers live under `Schema/SeriesContext/MetadataFetchers/` (services filed
under the schema tree — relocation candidate when the hybrid is split), and `LibraryRefreshSetting`
floats at the API root.

## 5. Development-context adherence — the strongest area

- **Outside-in TDD**: every slice in the current wave started with a failing booted-app test, then
  units, then mutation verification; the AF-named tests (AF2c/AF6 etc.) are a living acceptance net.
- **Mocking rule**: held everywhere checked. No direct 3rd-party mocks — HttpClient is always behind
  the owned `FakeHttpMessageHandler` util or `IHttpRequester`; Jikan behind `IJikan` (seam added
  today); Metron behind `IMetronClient`; torrents behind `IDownloadClient`. EF is swapped at the
  provider level (InMemory/Postgres), never mocked.
- **Division of labor**: respected — dispatcher harness owns scheduling proofs (FakeClock, DF1-7),
  units own branches, integration owns golden paths + one failure each. Little duplication across
  levels.
- **Typed payloads**: all production enqueues go through `Handler.PayloadFor(...)`; the only raw
  payload path is the registry-validated `POST /v2/JobQueue` (deliberate, AF6b).
- **Test debt**: fixture copy-paste — `Jpeg()` ×6 files, a hand-rolled `SeriesSource` fake ×5 files,
  ad-hoc ServiceCollection builders ×4. A `Tests/TestData` util (jpeg bytes, FakeSeriesSource,
  seed-series-with-library) would shrink several hundred lines.
- **Honest gaps vs the letter of the context**: "interfaces for all dependencies" (deliberately not —
  see §1), "register via layer extension methods" (not done), and the handler hand-construction
  contradicting the repo's own DI rule.

## Ranked fix list (each is one strangler-style slice)

1. **Extract the cover+sync enqueue helper** (5 sites → 1). Smallest, highest annoyance-per-line.
2. **DTO `From()` factories + fix the SourceId name inversion** (coordinated openapi/frontend regen).
3. **`Reconciler` base class** — 11 skeletons → 1; do the RunStartup decoupling in the same slice.
4. **Register `API/Services/*` in DI** and make handlers resolve instead of `new` (follow the
   `VolumeResolutionService` precedent); `LibraryLayoutResolver` via its existing interface.
5. **Split Program.cs into layer extensions** (`AddConnectors`, `AddMetadataFetchers`, `AddJobRuntime`,
   `AddReconcilers`, `AddKenkuContexts`) and move the migrate/seed block behind its own seam.
6. **Thin SeriesController**: rollup → `SeriesRollupService`; ChangeLibrary workflow → service.
7. **Split the entity/service hybrids** (SeriesSource cover pipeline, MetadataFetcher clients) — the
   largest and least urgent; do it when one of them next needs real work anyway.
8. **Test util consolidation** (Jpeg/FakeSeriesSource/seeders) — fold into whichever slice touches the
   tests next rather than as its own pass.
