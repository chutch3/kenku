# Feedback discovery — 2026-06-18

Discovery pass on the latest feedback wave, grounded in the code. File refs are exact. Each item:
**what's there now → root cause → options → call.** No fixes applied yet — this is the flesh-out.

The through-line: **four of the eight items are the same missing concept** — a first-class
*media-type axis (manga · comic)* that the user drives explicitly instead of the app inferring it
per-series. That's exactly what [`unified-content-design.md`](./unified-content-design.md) already
sketches (the 2×2 grid). This wave is the demand signal to actually build the user-facing half of it.

---

## F1. "Choose a download" appears on failures that aren't a choice

**Now:** A `DownloadChapter` job that hits the attempt cap goes to `NeedsAttention`
(`api/API/JobRuntime/Dispatcher.cs:83-99`). The frontend shows a **Choose download** button for
*any* `DownloadChapter` job in `Failed`/`NeedsAttention` — it never checks whether a choice actually
exists (`web/website/app/utils/jobs.ts:62-68`):

```ts
export const canChooseDownload = (job) =>
    job.type === 'DownloadChapter' && (job.status === 'Failed' || job.status === 'NeedsAttention');
```

A genuine multi-option post is only one of three resolver outcomes
(`api/API/Acquirers/DirectArchiveAcquirer.cs:44-48`, `ChaptersController.cs:419-431`):
`Choice` (real ambiguity), `Resolved` (one option), `Manual` (no options + a reason). A transient
API error (HTTP 500, timeout, connector down) never reaches the resolver as `Choice` at all — the
job just fails. But the button still renders, and clicking it re-runs the resolver live; if the post
genuinely has one option or the error persists, the modal shows "The post offers no downloads right
now." — which is the confusing dead-end the user hit.

**Root cause:** the "needs a choice" signal is *inferred on the client from job type+status*, when it
should be a **fact the backend records on the job**. Failure-because-ambiguous and
failure-because-transient look identical to the UI.

**Options:**
- **(A, recommended) Mark the reason on the job.** When the acquirer fails due to `Choice`, stamp a
  marker (a `NeedsChoice` status, or a structured field in `Job.Error`/a new `FailureKind` column on
  `Job.cs:22-53`). `canChooseDownload` keys off that, not off status. Transient failures show a plain
  **Retry**, not **Choose download**.
- **(B) Separate the transient path entirely.** Transient/retryable exhaustion → a distinct terminal
  state (`Failed`, kept) vs. user-actionable (`NeedsAttention`). Then the UI maps state→action
  cleanly. Bigger change; overlaps with F5 (operations/history).
- **(C, cheap stopgap)** Keep the button but, on open, if the modal resolves to zero options with a
  transient-looking reason, render **Retry** instead of the empty "no downloads" text. Papers over
  the symptom; doesn't fix the misclassification.

**Call:** A. Small, localized, kills the wrong-affordance. Pairs naturally with F5's failure
taxonomy if we do that later.

---

## F2. Search is slow and noisy for generic terms ("MAD")

**Now:** Global search fans out to **every enabled source in parallel**
(`api/API/Connectors/Global.cs:30-58`, `Task.WhenAll`), already filtered by `Enabled`, `ContentType`
(if the caller passes one), and torrent-skip. Results are concatenated **with no cross-source dedup,
no pagination, and no relevance ranking** — only a sort that floats language-matching sources up.
Per-source caps vary wildly (MangaDex paginates to 10k; GetComics 5 pages; ComicHubFree 20 pages;
some scrapers uncapped). So "MAD" = every source's full match set, duplicated across sources, all at
once. The slowness is *fan-out to sources that can't possibly have what you want* (comic sources when
you want English manga) plus *unbounded result assembly*.

**Root cause:** the caller rarely narrows `contentType`, and there's no language filter on the source
set at all (language is a post-hoc sort, `Global.cs:46-55`). Sources already carry the metadata to
filter on — `ContentType` and `SupportedLanguages[]` are declared per source
(e.g. `MangaDex.cs:18-19`, `WeebCentral.cs:20`, `Mangaworld.cs:19` = `["it"]`).

**Options (escalating):**
- **(A, recommended — the "simple UX fix" the user named) Filter the source set before fan-out.**
  Drive search from the media-type toggle (F7): manga mode → only `ContentType==Manga` sources;
  plus an optional language filter that drops sources whose `SupportedLanguages` can't serve the
  wanted language. "English manga" collapses the set to MangaDex + WeebCentral and drops every comic
  source and Mangaworld(it). This is mostly wiring `contentType`/`language` through
  `SearchController.cs:50-83` → `Global.SearchMangaScoped`, plus a `SupportedLanguages` predicate.
- **(A2) Cap + dedup the aggregate.** Top-N per source + cross-source dedup by title/source-id so the
  same series from two sources collapses to one row with multiple source badges. Cheap, big perceived
  win, independent of the toggle.
- **(B, the "go crazy" idea — YAGNI for now) Local catalog source.** Build a persisted catalog from
  discover/search/sync and hit it first, falling back to remote. The bones exist but only for the
  discover feed: `DiscoveryPost` (`api/API/Schema/DiscoveryContext/DiscoveryPost.cs`) caches feed
  items, not search results. A real catalog is a new table + population jobs + staleness policy +
  ranking — a project, not a fix. **The user themselves flagged this as YAGNI.** Park it; A+A2
  likely remove the pain entirely.

**Call:** A + A2 together. A depends on F7 landing the toggle (or at least the plumbing); A2 is
independent and can ship first. Defer B until A+A2 prove insufficient.

---

## F3. Cover changes / disappears when you click a discover item

**Now:** The discover rail shows `entry.coverUrl` straight from the discover feed (AniList/MangaDex
CDN), `DiscoveryRail.vue:22`. Clicking opens `DiscoverAddModal.vue`, which **searches connectors by
title** (`:59-66`, hardcoded `contentType: 'Manga'`, `includeTorrents: false`) and swaps in the
*matched connector series'* `coverUrl`. That connector cover may be **different art, or absent** (the
field is nullable). `AddSeriesForm.vue:5` then renders the resolved cover, so it visibly changes or
falls through `FallbackImage`'s chain to `/kenku.svg`. MangaDex CDN host rotation makes the original
URL fragile on top of that.

**Root cause:** two different cover provenances (discover-feed vs. connector-match) rendered in the
same visual slot across a click, with no continuity rule. Tracked series avoid this because
`SeriesCover.vue:32-37` prefers the Kenku-cached endpoint — but pre-add items have no cache yet.

**Options:**
- **(A, recommended) Keep the discover cover as the stable anchor — *and carry it into the add*.**
  Treat the feed `coverUrl` as the identity image through the modal; use the connector cover only to
  *fill* when the feed cover is empty, never to *replace* a good one. **Critically, the add flow must
  pass the feed cover through and seed it onto `Series.CoverUrl`** — today it's discarded
  (`AddSeriesForm` calls `ChangeLibrary` with only `connectorName`/`connectorSeriesId`; the persisted
  cover comes from the connector's `GetMangaFromUrl`). Without this, A only fixes the *click-time*
  swap and the cover still changes the moment the series lands in the library (delayed swap). Frontend
  binding + a new optional cover param on the add path.
- **(B) Cache the discover cover immediately on open** so it survives CDN rotation (reuse
  `CoverDownloadService` path). Heavier; only needed if rotation (not the swap) is the main culprit.
- **(C) Show both** ("feed cover" vs "this is what the source has") so a mismatch is explicit instead
  of a silent swap. More honest, more UI.

**Call:** A. The user's complaint is the *swap/removal*, and A removes it directly. B only if rotation
turns out to be a second, separate cause.

**⚠ Interaction with F4 (sequencing dependency).** F3 is a *pre-track / frontend* fix; F4-A's
precedence policy is *post-track / persistence*. They don't overlap in time, but they implement the
**same principle at two layers** ("a good cover isn't replaced by a lesser source") and must agree:
- The feed cover A seeds onto `Series.CoverUrl` is only durable if **F4-A's cover precedence ranks it
  sensibly** — i.e. a routine chapter sync must not immediately overwrite the cover the user picked
  (today `SeriesChapterSyncService.cs:49` overwrites every sync). If precedence puts connector-fresh
  above the user-seeded cover, F3 just *delays* the swap to the first sync.
- **Sequence:** define the cover precedence ordering in **F4-A first**, then implement F3 to seed the
  discover cover into that ordering. F4-B (finished-gate) further reduces post-add cover drift by
  cutting how often the metadata writers re-run on finished series.
- Net: F4-A/B don't fix the F3 *moment*, but they decide whether F3's fix *holds*. Plan them together.

---

## F4. How metadata fetching works (and why it feels coupled)

This one's a **"remind me + diagnose"**, so here's the map.

**What metadata is:** series-level descriptive data on the `Series` entity
(`api/API/Schema/SeriesContext/Series.cs`) — name, description, `CoverUrl`, `ReleaseStatus`
(`Continuing/Completed/OnHiatus/Cancelled/Unreleased`), authors, alt-titles, links, year, language —
plus `VolumeMetadata`, and the linkage tables `MetadataSource` and `MetadataEntry` that tie a series
to an external provider (MyAnimeList via Jikan, Metron for comics, or a connector).

**How it's triggered (the full list):**
1. **Scheduled refresh every 12h** — `MetadataRefreshReconciler.cs:15-44` enqueues
   `RefreshExternalMetadata` for *every* series that has a `MetadataEntry` and a download source.
2. **Manual link** — `MetadataFetcherController.cs:108-129`, runs `UpdateMetadata()` **inline**.
3. **Manual update** — `MetadataFetcherController.cs:166-181`, also inline.
4. **Add-from-URL** does **not** fetch metadata — it only enqueues cover + chapter sync
   (`SearchController.cs:164-195` → `SeriesJobs.EnqueueCoverAndSync`).

**The good news (counter to the hypothesis):** metadata fetching is **already its own job type**,
decoupled from download/sync/add — *not* embedded inside the download handler. And there **is a single
canonical model**: every connector maps into the *same* `Series` entity via the same constructor +
`UpsertManga` (`Series.cs:56-77`). So this is *not* "metadata depends on the function" and *not*
type-sprawl at the core.

### Refined diagnosis (user, 2026-06-18): it's *population points + no field owner*, not coupling

The "feels coupled, differs per function, multiple sources" smell decomposes into **two genuinely
separate things** — verified against current code:

**(i) Pre-track: there is no canonical row yet, so every surface shows its own source.** "Metadata"
in discover is a *different object from a different upstream* than "metadata" in search:

| Stage | Representation | Cover/metadata source | Persisted? | Linked to `Series`? |
|---|---|---|---|---|
| Discover | `DiscoveryPost` | AniList `coverImage.large` / Reddit thumb / GetComics scrape | yes, **own table** | **never** |
| Search | `MinimalSeries` DTO | the **connector's** API (e.g. `uploads.mangadex.org/...`) | no (transient) | no |
| Added, pre-sync | `Series` row | connector cover (hotlink only) | yes | n/a |
| Tracked + synced | `Series.CoverUrl` + cached file | connector → cached endpoint | yes | yes (collapses to one) |

So the user is **factually right**: the discover cover (AniList) ≠ the search cover (connector), they
live in different places (or nowhere), and nothing reconciles them until you add *and* sync. **But
this is inherent, not a bug** — before a `Series` exists there's no canonical anything. The remedy is
**UX continuity** (= F3's "keep the discover cover as the anchor"), *not* a data-model change. Forcing
a connector match up-front to unify them is exactly the slow path F2 is avoiding.

**(ii) Post-track: last-writer-wins on a flat field-bag with no provenance.** This is the *real*
latent problem. The canonical `Series` is a mutable flat record, and **three writers stomp the same
`CoverUrl` with three different ad-hoc rules**:
- chapter sync → overwrites from the connector *every sync* (`SeriesChapterSyncService.cs:49`)
- MyAnimeList → fills only if empty (`MyAnimeList.cs:107`)
- Metron → overwrites unconditionally if non-empty (`Metron.cs:74`)

No precedence policy, no record of which source wrote which field (`MetadataSource` tracks the
provider *linkage*, not per-field provenance). **The value you see depends on which job ran last.**
That non-determinism is what actually reads as "incoherent."

Plus a third, orthogonal issue: **the 12h refresh has no "done" gate.** Chapter sync got one
(`SeriesChapterSyncReconciler.cs:40-54`, commit `3de65b0`); metadata refresh never did, so finished
series re-fetch forever — churn that *amplifies* (ii) by re-running the non-deterministic writers.

**Options:**
- **(A, recommended — kills the smell) Centralize a precedence policy.** Replace the per-fetcher
  ad-hoc guards with one merge rule, e.g. *user-confirmed metadata source > connector > feed*; for
  cover specifically *cached > connector-fresh > provider-backfill*. Deterministic writes, **no new
  tables**. This is the minimal change that makes metadata stop depending on run order.
- **(B, orthogonal, also do) Give metadata refresh the finished-gate.** Skip `Completed`/`Cancelled`
  series with a recent `LastSyncedAt` (`MetadataSource.cs:15`); let new chapters/volumes from sync be
  the thing that re-triggers a refresh — "new content drives metadata," exactly the user's model.
- **(C) Fix F3 continuity** (pre-track) as its own UX change — unrelated to A/B, listed here only to
  show the pre-track half is handled elsewhere.
- **(D, YAGNI — user's instinct holds) Per-field provenance** (a provenance column / metadata-field
  table that records which source owns each field). Only worth it if conflicts keep biting *after* A.

**Call:** A + B (both small, independent, address (ii) and the churn). C lives under F3. Defer D.

---

## F5. Operations vs. history — do we need both?

**Now:** there isn't an "Operations" table and a "History" table. There are **two complementary
stores:**
- **Job queue** (`Job.cs`) — *intent + in-flight work + failures*. Failed/NeedsAttention kept until
  acknowledged; Succeeded/Cancelled pruned after a retention window
  (`CleanupService.cs:43-54`). This is "Operations."
- **Action records** (`ActionsContext/ActionRecord.cs`) — *immutable audit of what actually
  happened*, written **on success only** (e.g. `ChapterDownloadService.cs:115`). Subclasses:
  `ChapterDownloaded`, `CoverDownloaded`, `MetadataUpdated`, `LibraryMoved`, etc. This is "History."

They're genuinely different: jobs track *what we're trying to do and what's stuck*; actions track
*what's done*, including outcomes a pruned job no longer remembers. Failures live only on the job;
successes live in both transiently, then only in actions.

**Options:**
- **(A, recommended) Keep both, but make the split legible.** The confusion is presentational, not
  structural. Frame the UI as **Activity = the job queue** (live + needs-attention + recent) and
  **History = the action audit** (a durable, filterable log of completed events). Optionally write an
  action record on terminal *failure* too, so History becomes a complete account and the job queue can
  prune more aggressively.
- **(B) Merge into one event log.** Collapse jobs+actions into a single append-only timeline with a
  status field. Conceptually tidy, but you lose the clean "queue of pending work" semantics the
  dispatcher relies on (`Dispatcher.cs` leases/claims rows) — you'd be overloading a work queue with
  audit rows. High risk for low payoff.
- **(C) Drop History.** The user asked "what would I use it for?" — answer: the per-series event log
  on a series page, and post-mortems on pruned jobs. If neither is surfaced today, that's *why* it
  feels useless. The fix is to surface it (A), not delete the audit trail.

**Call:** A. Don't merge the work queue with the audit log; instead make History earn its place by
surfacing it (per-series timeline) and recording failures in it.

---

## F6. Pick the manga library layout when adding from discover/search

**Now:** `LibraryLayout` (`Flat` / `VolumeFolder` / `VolumeCBZ`) is the **on-disk organization** for a
series, and it's **manga-only** UI (`LibraryLayoutSelect.vue`, gated `kind !== 'comic'` on the series
page, `series/[mangaId]/index.vue:62`). It can only be set **after** the series is added — the add
flow doesn't expose it. The add call (`AddSeriesForm.vue:139-147`) hits
`/v2/Series/{id}/ChangeLibrary/{lib}` with `connectorName`, `connectorSeriesId`, `download` — **no
layout param.** (Note: "library layout" here is storage layout, *not* a grid/list view for the
library page — the library page has a single responsive grid, `SeriesCardList.vue:3`.)

**Root cause:** layout is a post-add edit; the add modal has no field and the endpoint takes no
argument for it.

**Options:**
- **(A, recommended) Add an optional `layout` to the add flow.** A select in `AddSeriesForm`
  (shown only for manga, mirroring the existing gate) → pass through `ChangeLibrary` as an optional
  query param defaulting to today's default. Small, contained, exactly what was asked.
- **(B) Per-user default layout** in settings, applied at add-time, overridable later. Fewer
  decisions at add-time; good if most series want the same layout. Can layer on top of A.

**Call:** A, optionally with B's default. Confirm with the user whether they want to choose every time
(A) or set a default and forget (B) — that's a genuine UX preference, see questions below.

---

## F7. Global manga ⇄ comics toggle (discover, search, library, settings)

**Now:** media type is **derived per-series, read-only**, never a mode the user sets.
`useSeriesKind.ts` infers `manga|comic` from a series' source `ContentType`. It surfaces as badges
(`SeriesCard.vue:7`, `SeriesDetailPage.vue:14`, `SourcesTable.vue:6`) but there is **no global
toggle and no global filter state.** Tellingly, `DiscoverAddModal.vue:63` **hardcodes
`contentType: 'Manga'`** — comics are second-class in the add path. Nav is a flat list in
`app.vue:81-86`; the header `#right` slot is where a toggle would live; there's no Pinia store holding
a current-media-type.

**This is the keystone item.** It's the user-facing realization of
[`unified-content-design.md`](./unified-content-design.md): *content type and source method are
independent axes.* The toggle is the top-of-app expression of the content-type axis, and it's what
makes **F2** (filter sources before search), **F3** (no more hardcoded `Manga`), and **F6** (manga-only
layout field) coherent instead of ad-hoc.

**Options:**
- **(A, recommended) A persisted global mode (`manga | comic`, maybe `all`).** A Pinia store + header
  toggle; thread it into discover (which 2×2 cell), search (source filter → F2), library (filter the
  grid), and settings (show relevant sources). One concept, four screens.
- **(B) Per-page filters only** (a filter control on each of discover/library/search). Less
  navigational weight, but it's four disconnected controls and doesn't carry intent across pages —
  the exact "things get lost" problem the user is describing.

**Call:** A — it's the cross-cutting fix the user explicitly asked for and the design doc already
endorses. Sequence it **before/with F2 and F6**, since they consume it. Decide `all` vs strict
two-way as a UX question (below).

---

## F8. Feature-toggle the torrent download feature (off by default for now)

**Now:** torrent acquisition is **already gated**, just implicitly. The whole path is skipped if no
download client is configured (`ServiceCollectionExtensions.cs:63-68`), and the `Indexers` connector
can be disabled via the existing `DisabledConnectors` mechanism (`KenkuSettings.cs:78-81`,
`SeriesSourceController.cs:71-83`). The pipeline itself is **solid through hand-off, polling, and
single-file finalization**, but the user's specific worry — *combining downloaded pieces reliably into
one series* — **is genuinely not implemented.** Pack finalization (`TorrentFinalizationService.cs`)
handles the common "one `.cbz` per issue" case by filename parsing and fans out to matching chapters;
it has **no logic to merge a chapter split across multiple files, no volume re-packing, no metadata
backfill, and orphans anything that doesn't parse/match** (no alert, no retry).

**Root cause:** the feature is ~80% built but the last mile (piece combination + clarity about what's
being downloaded) is missing, and there's no single, discoverable **off switch** — gating is a side
effect of "don't configure a client," which is non-obvious.

**Options:**
- **(A, recommended) Explicit `TorrentEnabled` flag.** Add a boolean to `KenkuSettings`, check it in
  `AddTorrentAcquisitionPath` alongside the existing client check, expose `GET/PATCH` on the settings
  controller, and surface a single toggle in Settings → Downloading. Default it **off** until the
  combine story is designed. Clean, discoverable, reversible — exactly the user's ask.
- **(B, zero-code stopgap) Disable the `Indexers` connector** via the mechanism that already exists.
  Hides torrent search immediately. But it's buried in the sources table, doesn't read as a
  feature-kill, and leaves in-flight torrents finalizing — fine as a *today* mitigation, not the
  answer.
- **(C) Hide torrent UI too.** Gate `TorrentsPanel.vue`, `DownloadClientsCard`, `IndexersCard`,
  `ReleaseSelectionCard` behind the same flag so "off" means *gone*, not *configured-but-dormant*.
  Do this with A.

**Call:** A + C — one honest flag, defaulted off, that also hides the half-built UI. B as the
right-now mitigation if we want it dark before A ships. The *combine-pieces* design is a separate,
later effort (it's a real gap, not a toggle).

---

## Cross-cutting shape & suggested sequence

The items aren't independent — there's a spine. Honest dependency graph:

1. **F7 (media-type toggle)** is the keystone; **F2** (source filter), **F6** (layout-at-add), and
   F3's hardcoded-`Manga` all *consume* it. Do the toggle + store first.
2. **F4-A (cover/metadata precedence policy)** gates **F3** — F3 seeds the discover cover onto
   `Series`, and only F4-A's ordering decides whether it survives the first sync. Do F4-A before F3.
3. **Genuinely independent, small, high-value, any order:** **F1** (failure taxonomy), **F2-A2**
   (cap/dedup, no toggle needed), **F4-B** (metadata finished-gate), **F8** (torrent flag).
4. **F5** (operations/history) is mostly a *framing/surfacing* job, not a schema change — lowest
   urgency, do when touching the Activity/History UI.
5. **Parked as YAGNI** (user-flagged): F2-B (local catalog source), F4-D (per-field provenance),
   F4 event-driven metadata, F5 merged event log. Revisit only if the cheap fixes don't hold.

## Decisions (resolved with user, 2026-06-18)

- **F7 toggle (refined 2026-06-18):** **Strictly manga ⇄ comic on the *acquisition* surfaces
  (discover + search)** — the toggle is a directional intent ("what I'm looking for now"), so it hard-
  filters the search source set and the discover cell. This keeps the F2 "only search manga sources"
  win clean. **The *library* is the exception:** the toggle acts as a *filter with an "all" escape*
  (default to showing everything), because browsing what you *own* has a legitimate need to see both
  types that acquisition doesn't. "All" never touches the acquisition fan-out, so the search win is
  preserved. Build per the sliced plan below.
- **F6 layout:** **Choose every time** — a manga-only layout field in the add modal on each add (not a
  settings default). So: add the field to `AddSeriesForm` + thread an optional `layout` through the
  `ChangeLibrary` endpoint.
- **F5 history:** **Keep + surface it** — make History earn its place as a per-series event timeline
  plus a record of failures, distinct from the live job queue. Do *not* merge the work queue with the
  audit log; do *not* drop the audit trail.

---

## Plan critique — fit with the dev context, design & architecture

A candid pass on whether the proposals above respect how this codebase is built (TDD / Circle
Architecture, incremental-verified delivery, reconciler+job runtime, the canonical `Series` model, the
unified-content 2×2 vision). Verdict first, then the sharp edges.

### Where the plan fits well
- **Respects the canonical model.** Nothing here forks `Series` or invents a parallel metadata store;
  F4-A *consolidates* toward a single owner rather than adding sources of truth. F1 pushes the
  "needs-choice" fact onto the job entity (where the domain already lives) instead of inferring it in
  the client — that *removes* frontend/backend coupling. Both move with the grain.
- **Reuses existing patterns.** F4-B is a literal twin of the shipped finished-gate (`3de65b0`); F8
  follows the `KenkuSettings` + service-gate pattern; F5 refuses the tempting merge that would break
  the dispatcher's queue semantics. Low novelty = low risk, matches the "diffs shrink surface area"
  hygiene rule.
- **Architecturally coherent keystone.** F7 builds the *user-facing half* of an already-endorsed
  design (`unified-content-design.md`), not a competing one. The content-type axis is the right
  organizing concept.
- **YAGNI discipline.** Local-catalog, per-field provenance, event-driven metadata, merged event log
  are all parked behind cheaper fixes — consistent with the project's incremental ethos.

### Sharp edges the plan currently understates

1. **F7 is an *epic*, not a slice — and the plan says "do it first."** It's a Pinia store + header
   toggle + backend search-param + filtering across discover/search/library/settings. Sequenced as one
   unit it becomes a long-lived branch, violating *incremental-verified-delivery*. **Decompose into
   independently shippable slices:** (a) backend — content-type as a first-class search filter on the
   source set (testable in isolation, no UI); (b) the store + header toggle as a no-op (renders,
   persists, changes nothing yet); (c) wire one page at a time (search → library → discover →
   settings), each its own verified slice. Until this is sliced, F7 doesn't satisfy the delivery norm.

2. **F4-A is more than "a policy function" — name its home or it's cosmetic.** To express
   *user-confirmed > connector > feed*, each writer must declare its own source rank — today the
   writers (`SeriesChapterSyncService`, `MyAnimeList`, `Metron`, and the *inline* controller paths)
   pass no provenance. So F4-A needs (i) a source-rank enum threaded through every write, and (ii) **a
   single owned seam** (e.g. `IMetadataMerge.Apply(field, incoming, rank)`) that all writers route
   through — exactly the kind of mockable seam Circle Architecture wants. If precedence logic instead
   gets sprinkled back into each `UpdateMetadata`, the smell just moves. "No new tables" stays true,
   but it's a small signature change across ~4 call sites, not a one-liner. The plan should say so.

3. **The metadata write path is split (job vs inline) — F4 is the moment to notice.** Scheduled
   refresh runs as a job; **manual link/update run synchronously inside the controller**
   (`MetadataFetcherController.cs:108-129, 166-181`). That's a pre-existing architectural
   inconsistency the precedence work will straddle. Either route the inline paths through the same
   merge seam (minimum), or make them jobs like everything else (cleaner, larger). Flag it, don't
   silently inherit it.

4. **F8's runtime toggle vs startup DI gating.** Torrent acquisition is wired at *startup* in
   `AddTorrentAcquisitionPath` (`ServiceCollectionExtensions.cs:63-68`). A `TorrentEnabled` flag
   checked only there means **flipping it needs a restart** — unless the gate also moves to
   request-time, mirroring how `DisabledConnectors` is applied both at startup *and* on the live
   connector object. The plan's "check in `AddTorrentAcquisitionPath`" is startup-only; decide
   explicitly whether the toggle is restart-required (simpler, honest) or live (more work). Relates to
   the standing [[runstartup-coupling-smell]] tech-debt.

5. **Strict manga⇄comic library filtering has an architectural edge.** Series *kind* is derived from
   source `ContentType` (`useSeriesKind`). With the library "always filtered to the current mode," an
   owned comic series is **invisible while in manga mode** — and a series with mixed/ambiguous sources
   has to resolve to one bucket. Need a rule (and probably a test) for "owned content not in the
   active mode": a count badge, a quick-switch hint, or an explicit "all" escape hatch — which the
   strict-two-way decision deliberately removed. Worth re-confirming that trade-off.

6. **No test seams named yet.** For a strict Red/Green/Outside-In shop, each slice should declare its
   owned seam to mock and its outside-in acceptance test *before* code. The doc is options-level, so
   this is expected — but the **next** step (graduating to an implementation plan) must add, per
   slice: the acceptance test, the owned seam, and the "verified in a real run" check
   ([[incremental-verified-delivery]], [[dev-workflow-tdd]]). Concretely the cheap ones are very
   testable: F1 = "transient-failed job exposes Retry, not Choose"; F4-A = pure merge unit tests +
   one integration test that sync routes through the seam; F4-B = "Completed+recent series enqueues
   no refresh."

7. **Release shape unstated.** Most items are user-facing → discrete Conventional-Commit releases. But
   F4-A and F5 have *refactor* character; if they land incrementally they may warrant the
   squash-into-one-refactor-release treatment ([[job-runtime-rearch-squash]]). Tag each item
   feature-release vs refactor-squash before starting.

### Net
The **design instincts are sound and grain-aligned** — the plan consolidates rather than sprawls, and
reuses existing patterns. The **gap is delivery granularity**: F7 and F4-A are described as if
atomic when each is a multi-slice effort, and the TDD seam/test plan for every item is still TBD.
Before any coding: slice F7, name F4-A's merge seam + source-rank, decide F8 restart-vs-live, and
attach a per-slice acceptance test. Do that and the plan is fully consistent with the dev context.

---

## F7 implementation plan (sliced, test-first)

**Material correction (verified 2026-06-18):** the **backend source-filtering already exists and is
tested** — `SearchController.SearchManga` takes `[FromQuery] ContentType? contentType`
(`SearchController.cs:50-52`), passes it to `Global.SearchMangaScoped`, whose predicate already drops
non-matching sources (`Global.cs:34-38`), with a passing test that comic/torrent sources aren't
queried in manga mode (`Tests/Unit/Controllers/SearchControllerTests.cs:179`). So **F7 is mostly a
frontend effort**: give the app a media-mode state, drive the *existing* backend filter from it, and
filter the library. State is **Nuxt `useState`, not Pinia** (no Pinia in the repo; no persistence
plugin). Mind [[frontend-test-build-gotchas]]: `rm -rf .nuxt && nuxt prepare` before `vitest`;
composables lose Nuxt context after the first `await`.

Each slice is independently shippable and must be **verified in a real run** before the next
([[incremental-verified-delivery]]), Red→Green→Refactor per [[dev-workflow-tdd]].

**Slice 1 — `useMediaMode` state + header toggle (no-op).** *Foundational; changes no behavior yet.*
- Build: a `useMediaMode()` composable wrapping `useState<'manga'|'comic'>('media-mode', …)` with
  persistence via `useCookie` (SSR-safe) so it survives reload; a two-segment toggle mounted in
  `app.vue` `#right` between the **Add series** button and `<UColorModeButton>` (`app.vue:52-56`).
- Tests (vitest, `web/website/test/`): composable defaults to `manga`, flips, persists across a
  remount; toggle renders and calls the setter. Match `seriesKind.test.ts` / `mountSuspended` style.
- Verify (real run): toggle visible, flips, survives a page reload; nothing else changes.
- Release: feature, inert.

**Slice 2 — wire search to the mode (delivers the F2 win).** *Highest-value slice.*
- Build: `search.vue` / `useSeriesSearch.searchByConnector` pass `contentType: mode` into the already-
  supported query param (`useSeriesSearch.ts:27`). No backend change — the filter + its test exist.
- Tests: component test asserts searching in comic mode sends `contentType=Comic`; the existing
  backend `SearchControllerTests.cs:179` already covers the filtering itself (cite it, don't dup).
- Verify (real run): toggle → comic, search "MAD", confirm only comic sources are queried and it's
  faster / less noisy.
- Release: feature. **This is the slice that actually fixes the reported pain.**

**Slice 3 — wire discover to the mode (also closes F3's hardcode).** 
- Build: `DiscoverAddModal.vue:63` stop hardcoding `contentType: 'Manga'` → use `useMediaMode`; the
  discover page selects its 2×2 cell from the mode.
- Tests: modal resolves via the active mode (comic mode → `contentType=Comic`); no longer pins Manga.
- Verify (real run): discover in comic mode resolves comic sources on add.
- Release: feature. Coordinates with **F3** (do F4-A precedence first, per the F3↔F4 note).

**Slice 4 — library filter with the "all" escape (the refined decision).** 
- Build: extend the existing library filter pipeline — `index.vue` already has `filterText` /
  `statusFilter` / `sortBy` and a `filtered` computed (`index.vue:61-86`). Add a `modeFilter`
  (`'all' | 'manga' | 'comic'`, **default `all`**) using `seriesKind(series, connectors)`; surface a
  small control + a count of items hidden by the active filter.
- Tests: with mixed library, `manga` hides comics but `all` shows everything; the hidden-count is
  correct. (Guards the "don't permanently hide owned content" decision.)
- Verify (real run): own both types — confirm nothing is unreachable; default shows all.
- Release: feature.

**Slice 5 (optional) — settings reflects the mode.** Sources list emphasises/filters to the active
mode. Low priority, purely cosmetic; skip until 1–4 prove out.

**Optional backend follow-on (F2-A language half).** Separate from F7: add a `SupportedLanguages`
predicate to `Global.SearchMangaScoped` so "English manga" also drops e.g. Mangaworld(it). Its own
slice with its own `Global` unit test; independent of the toggle.

**Sequence:** 1 → 2 (ship the win) → 4 (library safety) → 3 (after F4-A) → 5/F2-A as desired.
