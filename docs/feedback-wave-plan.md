# Feedback wave — bugs & improvements plan

> Scope: the bugs/improvements feedback (2026-06-09). New features (auth, discovery, docs site, …) are parked in §4.
> Work the slices in order, one verified slice at a time (red test → green → real-run check → delete what's replaced).
>
> **Status (2026-06-09):** S1–S8 implemented, tested, and pushed to main (commits `6e339e5`…`4366575`), plus a
> Playwright golden-flow suite (S9). **Real-run gates still open:** torrent path against live Prowlarr+qBittorrent
> (S1), "I am a hero" NeedsAttention surfacing + stored-id check on prod (S2), Chainsaw Man/Berserk covers after a
> MAL metadata update (S7) — verify after the next deploy.

**Contents:** [1 Findings](#1-findings-what-the-code-actually-does) · [2 Slices](#2-slices-in-order) · [3 Feedback → slice map](#3-feedback--slice-map) · [4 Parked](#4-parked-new-features)

---

## 1. Findings (what the code actually does)

Grounding for every slice; each was read this session, not assumed.

- **F1 — Torrent downloads are structurally broken (confirmed bug).** `TorrentAcquirer.AcquireAsync` *always* returns null — by its own doc comment null means "handed off to the torrent client, not ready yet" (`api/API/Acquirers/TorrentAcquirer.cs:12-17,70-71`). But `ChapterDownloadService.DownloadAsync` treats null as failure and throws (`api/API/Services/ChapterDownloadService.cs:82-84`). So every comic download job fails *after possibly adding the torrent*, gets retried with backoff (re-adding the torrent each attempt), and lands in NeedsAttention. The job model has no way to say "deferred to external client"; `FinalizeTorrent`/`TorrentCompletionReconciler` exist to finish the chapter but nothing guards the front half. Separately, `DownloadReconciler` re-enqueues any `!Downloaded` chapter every 10s once the prior job leaves the queue — a torrent in flight looks identical to a torrent never started.
- **F2 — Sync can succeed silently with nothing to show ("I am a hero").** `SeriesChapterSyncService.SyncAsync` logs and returns cleanly when the source row or connector is missing, or when the connector returns 0 chapters; the job records **Succeeded** (`api/API/Services/SeriesChapterSyncService.cs`). With 0 chapters there is nothing for `DownloadReconciler` to enqueue, so the series sits empty with no signal anywhere. (Adding/removing the series again re-runs the same silent path.)
- **F3 — The 'Downloading' badge is not download state.** It derives purely from `sourceIds[].useForDownload` (`web/website/app/composables/useSeriesStatus.ts`; backend filter `SeriesController.cs:58-75`). "Downloading" really means "tracking with a source enabled" — hence "says downloading, but everything is downloaded". The AF6a per-series rollup (downloaded X/Y, job counts, lastError) was deliberately deferred in the rearch and never built.
- **F4 — Add ≠ download.** `TrackSeriesPanel` calls only `ChangeLibrary`; enabling a source (`UseForDownload`) is a separate toggle the user must find on the series page (`MarkAsRequested`, `SeriesController.cs:282-329`, is what enqueues cover + sync). So a freshly added series lands "Paused". `ef1937f` made `ChangeLibrary` sync chapters, but the source-enable step is still manual.
- **F5 — Comics and manga are one code path with manga-shaped UI.** Single `Series` entity, no kind discriminator; comics differ only by their source's `AcquisitionKind.Torrent` (`IndexerBackedSeriesSource.cs:32`). The frontend renders the same detail page for both, including the MangaDex volume-mapping card and AniList-flavored metadata — meaningless for comics. Indexer-sourced series are built with an empty `CoverUrl` (`IndexerBackedSeriesSource.cs:92-97`), and `CoverDownloadService` silently skips empty cover URLs (`CoverDownloadService.cs:31-34`) → comics always show the default logo. Metron (the comic metadata fetcher) can supply name/description/**cover** (`MetadataFetchers/Metron.cs:69-74`) but only after a manual link.
- **F6 — Release scoring exists, minimally; profiles don't.** `ReleaseSelector` (`api/API/Indexers/ReleaseSelector.cs`): seeder floor (default 2), blocked tokens (`cbr`,`pdf`), preferred tokens (`cbz`), then most seeders. Configurable in settings code but not exposed in the UI. No Sonarr/Radarr-style quality/release profiles.
- **F7 — Queue vs Activity.** Queue = `JobQueue` rows (unit of work: status, attempts, error, timing) via `/v2/JobQueue`. Activity = `ActionsContext` audit log (immutable "what happened" events: chapter downloaded, cover downloaded, data moved) via `/v2/Actions/Filter`; the series page links into it per-series. They overlap exactly where a successful job emits an action record. Job rows show only the machine type name (`DownloadChapter`), no human label, no payload context (which series), no result stats.
- **F8 — Manual triggers.** `POST /v2/JobQueue` is registry-validated (AF6b holds), and several Maintenance endpoints enqueue jobs, but there's no UI to trigger work in context (e.g. "sync now" on a series) and no endpoint listing trigger-able types.
- **F9 — Cover misses on manga (Chainsaw Man, Berserk).** Cover comes only from the connector page's `CoverUrl`; a parse miss or empty value is skipped silently, and there is no fallback to the linked MangaDex metadata source or a metadata fetcher. Which failure applies to those two series needs a prod look (see S7).

---

## 2. Slices (in order)

Ordering: confirmed bugs first (S1, S2), then the rollup that makes everything visible (S3) since later UX slices build on it, then flow/UX, then consolidation/polish.

### S1 — Torrent download path: make "handed off" a real outcome  *(bug: comic downloads do not work)*

**Failing test first (outside-in):** booted app, torrent-kind source with stubbed indexer + download client. Enqueue `DownloadChapter` for one chapter. Assert: the torrent is added to the client **exactly once**; the job does **not** end Failed/NeedsAttention; subsequent `DownloadReconciler` scans do **not** re-enqueue while the torrent is in flight; when the stub client reports complete, `FinalizeTorrent` marks `Downloaded=true` with the file placed. (This is red today on the first assert — the job throws at `ChapterDownloadService.cs:84`.)

**Change:**
- Replace the `string?` acquirer contract with an explicit result: `Acquired(path)` / `Deferred` / `Failed(reason)`. `ImageListAcquirer` returns Acquired-or-Failed; `TorrentAcquirer` returns Deferred on successful handoff, Failed when no release/refused. Delete the null-means-two-things convention.
- `ChapterDownloadService`: Deferred → return without marking Downloaded and without throwing (the job Succeeds; `FinalizeTorrent` owns completion). Failed → throw as today (bounded retry → NeedsAttention with the *reason*, e.g. "no release passed selection (4 candidates, min seeders 2)" — that string is the user's corrective signal).
- In-flight guard: `DownloadReconciler` must skip chapters with a pending torrent (the staging dir/tag keyed by `chapter.Key` already exists — persist or query that marker) so the 10s tick can't re-add.

**DoD:** test green + mutation-verified (revert the Deferred branch → red). Real run against Prowlarr + qBittorrent: one comic chapter end-to-end, torrent added once, chapter finalized, queue shows no Failed churn.

### S2 — Zero-outcome sync becomes visible  *(bug: "I am a hero" downloads nothing, no indication)*

**Failing test first:** booted app, source whose connector returns no match / 0 chapters. Run the sync job. Assert an observable outcome — hard failures (source row missing, connector lookup fails, connector throws) end **NeedsAttention** with a mapped message; a legitimate-but-empty result records "0 chapters found" on the job and stamps the source's last-sync outcome. Red today: the job ends Succeeded with no trace either way.

**Change:** in `SeriesChapterSyncService`, stop swallowing — throw on the hard-failure branches (dispatcher already does bounded retry + error recording); for the empty case, write the outcome where S3 can read it (job result summary + `LastSyncedAt`/last-outcome on the source link).

**Diagnose the live series (before coding, it sharpens the test):** on prod, `GET /v2/JobQueue` for SyncSeriesChapters jobs for the series + `docker service logs downloads_kenku | grep -i 'hero'` — determine which branch it hit (no source match vs 0 chapters parsed vs connector error). Whatever it is becomes the named repro test.

**Live evidence (2026-06-09, WeebCentral):** the chapters exist upstream — `series/01J76XY9720JA20H3XRRDNXRYX/full-chapter-list` returns 200 with 22 chapter links. But the URL `GetChapters` builds when `IdOnConnectorSite` carries the title slug (`…/I-Am-A-Hero/full-chapter-list`) returns **307 → /404 → non-2xx → `return []`** (`WeebCentral.cs:192-203`) — and the series *page* tolerates the title suffix, so search/add work while chapter sync silently empties. Confirm what prod stored for this series (`sourceIds[].idOnConnectorSite`), then: (a) fix `GetChapters` to strip the slug to the ID, (b) treat a redirect-to-404 / non-2xx chapter fetch as a **thrown** failure, not an empty list.

**DoD:** tests green, mutation-verified; on prod, re-add the series and the failure (or "0 chapters") is visible in the queue/series page within one poll.

### S3 — Per-series rollup + truthful status badge  *(the deferred AF6a; fixes the 'Downloading' confusion)*

**Failing test first:** `GET /v2/Series/{key}/rollup` returns `{ downloaded: X, wanted: Y, jobs: {queued,running,needsAttention}, lastError: <human string>, lastSync: <outcome+time> }` for a seeded series with mixed state. Then component test: badge renders **Downloading** only when work is actually pending/running, **Attention** when any job is NeedsAttention, **Up to date** when X==Y and idle, **Paused**/**Untracked** as today.

**Change:** rollup endpoint reading SeriesContext + JobQueue (payload/resourceKey already carry the series key); `useSeriesStatus` switches from `useForDownload`-only to rollup-driven; series card + detail page show X/Y and surface `lastError` with a retry action (retry endpoint exists).

**DoD:** a series mid-download shows Downloading with X/Y; a fully downloaded one shows Up to date; the S2 failure shows Attention on the card — verified live.

### S4 — Add flow: an add modal on the search page  *(rethink add-to-library)*

**Design (decided 2026-06-09):** clicking a search result opens a **modal** on the search page — no navigation, no "preview mode" detail page. The modal shows:
- a summary (cover, title, description, source it came from),
- a **chapter availability preview** — live `GetChapters` from the connector, so "0 chapters from this source" is visible *before* adding (would have flagged the I Am A Hero bug at add time),
- **library select** (e.g. manga vs comics library) + layout default,
- kind-aware metadata setup (S5b plugs in here: MangaDex volume-mapping suggestion for manga, Metron suggestion for comics),
- two actions: **"Add & download"** (track + enable the originating source + enqueue cover/sync) and **"Add only"** (track, source stays off — watchlist case).

After add, the search card flips to an "In library" state linking to the real series page. The detail page's untracked/`isSearchResult` preview mode (`pages/series/[mangaId]/index.vue`, `TrackSeriesPanel`) is **deleted in the same slice** — the detail page only ever renders tracked series, which removes the stale-page confusion outright.

**Failing tests first:** API — `ChangeLibrary?connectorName=X&connectorSeriesId=Y&download=true` sets `UseForDownload=true` on that source and enqueues cover+sync (extends ef1937f); `download=false` tracks without enabling. Component — modal renders summary/chapter preview/libraries; each action sends the right params; card flips to In-library.

**DoD:** search → modal → Add & download → card shows In library, series page shows chapters arriving and badge Downloading (S3). Preview-mode code deleted, net frontend surface shrinks. Verified live.

### S5 — Comics diverge where it matters  *(identity, metadata inputs, covers)*

Three sub-slices, still YAGNI — no schema fork, no new entity:

- **5a Kind on the wire:** expose `kind: manga|comic` on series DTOs, derived from the source's `AcquisitionKind` (torrent/indexer-backed ⇒ comic). No DB change until something needs persistence.
- **5b UI branches on kind:** comic series hide the MangaDex volume-mapping card and AniList-flavored bits; Metron becomes the primary "Series details" card. Search results from indexers labelled as comics. (Test: component renders per kind.)
- **5c Comic covers via Metron:** when a comic links to Metron, the cover lands (Metron already returns `image`); auto-suggest the Metron link at add time using the parsed title/year — the same inline candidates pattern as the MangaDex mapping card, scored, user confirms. Kills the default-logo experience.

**Profiles/scoring (the open question):** keep `ReleaseSelector` as-is, expose its three knobs (min seeders, preferred/blocked tokens) in settings UI as a small slice. Full arr-style profiles are deferred until the basic comic path has survived real use — revisit after S1+S5 have been live for a while.

**DoD per sub-slice:** component/API tests green; live check: an indexer-added comic shows a cover and a comic-shaped detail page.

### S6 — One operations view  *(queue ≠ activity confusion; richer job rows; manual triggers)*

- **6a Human job rows:** handlers set a short result summary on the job (`"+4 chapters"`, `"removed 12 files"`, `"cover from MangaDex"`), and the API maps type+payload to a label with series context (`"Download Ch. 12 — Berserk"`). Queue UI shows label, context link to the series, result, duration. (Job entity gains one nullable `Result` column — that's the whole schema change.)
- **6b Merge the nav:** one **Activity** section, two tabs — *Operations* (today's queue: pending/running/attention, retry/cancel/dismiss) and *History* (succeeded jobs + action records, filterable). The series page keeps its per-series history link. Don't delete `ActionsContext` — it's the durable audit trail the pruned job rows can't be (jobs are deleted after the retention window, f683b51); History reads both.
- **6c Triggers in context, not a job form:** series page gets "Sync now / Refresh cover / Resolve volumes" buttons (enqueue via the existing registry-validated `POST /v2/JobQueue`); settings/maintenance page gets the Cleanup kinds. No free-form "run any job" UI — AF6b trust boundary stays.
- **6d The intervention loop (see error → fix input → re-trigger):** plain Retry re-runs the same payload, which is useless when the *input* is bad (the I Am A Hero case: a broken stored `idOnConnectorSite` fails identically on every retry, and remove-and-re-add re-stores the same value). So: a NeedsAttention job shows the mapped error *and* the input it ran with (series, connector, stored id/URL); the series page's source card exposes that stored id with a **re-match** action (re-search the connector, pick the right result, update the `SourceId` — a new, small endpoint; today no edit path exists); fixing it offers "Sync now" inline. Failing test: a series whose source id 404s → sync lands NeedsAttention with the input visible → re-match via API → re-trigger → chapters arrive. This is the slice that turns S2's surfaced errors into something the user can actually act on without being stuck.

**DoD:** a non-author can read the queue and say what the system is doing; every feedback question in this cluster ("what's the difference", "more information", "manually trigger") has a concrete on-screen answer.

### S7 — Manga cover fallback  *(Chainsaw Man / Berserk)*

**Diagnose first** on prod: for those two series, is `CoverUrl` empty (connector parse miss) or set-but-failing to fetch? The answer picks the repro test.

**Change:** fallback chain when the connector cover is absent/fails — linked MangaDex metadata source cover → linked metadata fetcher (MAL/Metron). Surface "no cover" on the series page with a "fetch from …" action instead of silently skipping (`CoverDownloadService.cs:31-34` keeps the null-guard, but the *skip* gets recorded so S3 can show it).

**DoD:** both series show covers on prod after a refresh.

### S8 — Refresh feedback  *(polish)*

Refresh/reload buttons get a visible acknowledgment — spinner while in flight plus a "Updated just now" tick or toast. Component test per button. Smallest slice; do it whenever a frontend slice is already open.

---

## 3. Feedback → slice map

| Feedback item | Slice |
|---|---|
| "I am a hero" not downloading, no indication, no corrective action | S2 (+S3 surfacing, S6d intervention loop) |
| Comic downloads do not work | **S1** |
| No difference comic vs manga experience; confusing add inputs | S5a/5b |
| Comic download profiles/scoring? | S5 note — expose ReleaseSelector knobs, defer profiles |
| Comics should differ on search/add but share the job engine | S5 (job engine already shared) |
| Rethink add-to-library flow; page doesn't update after add | S4 |
| Comics show default logo | S5c |
| 'Downloading' indicator hard to see / contradicts reality | S3 |
| Komga gets covers Kenku can't (Chainsaw Man, Berserk) | S7 |
| Metadata decoupled/duplicated; why link AniList later | S5b reduces noise for comics; manga linking rationale = volume mapping (MangaDex) vs details (MAL) — S6a labels + the two-card copy already shipped (3c92ea2); revisit only if still confusing after S3-S5 |
| Storage/volume/trigger set at add; page doesn't update | S4 (+ settings already editable on detail page) |
| Refresh buttons give no feedback | S8 |
| Activity vs Queue difference / similar-but-misaligned / merge | S6 (F7 is the answer; 6b is the merge) |
| Queue needs more info per job | S6a |
| Manually trigger jobs from GUI | S6c |

## 4. Parked (new features — separate plans when picked up)

Configurable sources/metadata setup UI · optional OIDC accounts · manga/comic discovery · comic scraping sources · built-in reading (drop the Komga requirement for viewing) · docs site (lean quick-start, Karasu theme — theme lives in `app/app.config.ts` + `app/assets/css/main.css`).
