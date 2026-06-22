# Feedback triage — 2026-06-11

User notes from live use of v0.16.0, triaged against the code. File refs are exact.

## Confirmed bugs (await user go-ahead before fixing)

### B1. Manga volume machinery leaks onto comic series pages
- `LooseChapters` has no kind gate — it renders for every series with a library
  (`web/website/app/pages/series/[mangaId]/index.vue:24`). The "Volume mapping" MangaDex card IS
  gated (`:67`), so seeing it on The Boys means `kind` computed `manga` — needs the user's data
  (sources listed on the series, deployed tag) to pin down.
- Backend: `VolumeResolutionReconciler.cs:40` picks up *any* chapter with null VolumeNumber and
  `VolumeResolutionService` auto-matches the series against **MangaDex** — including comic-sourced
  series, where a wrong match can assign bogus volumes. ContentType is never consulted.
- Proposed fix: gate LooseChapters (and any volume-assignment UI) on `kind === 'manga'`; skip
  volume resolution for series whose sources are all comic-content; diagnose the kind miscompute
  with the user's answers.

### B2. Series delete is slow and silent
- `SeriesLibraryService.DeleteAsync` (`api/API/Services/SeriesLibraryService.cs:48-64`):
  loads ALL jobs for the series into memory (`:53`), then calls `downloadClient.Remove(tag)`
  **sequentially for every chapter source key** (`:60-64`) — N HTTP round-trips to qBittorrent,
  even for chapters that never were torrents. With many failed jobs + a slow client this is the
  reported "major slowness".
- UI (`pages/series/[mangaId]/index.vue:84,141-145`): no confirmation dialog, no loading state,
  no toast — silent until navigation.
- Proposed fix: only release torrents for torrent-kind sources (or sweep via a background job);
  batch-delete jobs with ExecuteDelete; frontend confirm + loading + toast.

### B3. Queue page jumps and truncates errors
- Sort is needs-attention-first then most-recent-activity, re-evaluated on a 2s poll
  (`QueueList.vue:103-107`, poll `:89-91`) — rows jump as activity changes.
- Error text is `truncate max-w-80` with tooltip only (`QueueList.vue:29`) — cut off, no expand.
- Proposed fix: stable FIFO ordering (enqueue time), status changes in place, attention count
  indicator at top; rows expandable (collapsed by default) showing full error + job detail.

### B4. Indexer 429s are invisible
- `TorznabIndexer.cs:34-41`: HTTP 429 → WARN log + cooldown recorded (`IndexerCooldown`,
  default 10 min or Retry-After) + **empty result returned**. The user sees "no results" with no
  hint the indexer is rate-limited.
- Proposed fix: expose cooldown state via API; settings indexer table shows "rate-limited,
  retrying in Xm" badge; optionally annotate torrent-search responses.

### B5. Series-detail refresh button feels dead
- `refreshData` (`pages/series/[mangaId]/index.vue:163-173`) refreshes chapters/series keys but
  NOT `Series.Rollup` — the status badge never updates. Chapters also cache under a global
  `'Chapters'` key (`ChaptersList.vue:114`) rather than per-series.
- Also structural: "Sync now" enqueues a job; an immediate refresh shows pre-job data. Refresh
  should include rollup and ideally poll/refresh when running jobs for the series hit zero.

## Questions answered (no code change needed)

- **Does Global search include torrents?** Yes — `Global.cs:25` fans out to every *enabled*
  connector, Indexers included. Connector enable/disable exists in the API
  (`KenkuSettings.DisabledConnectors`, SeriesSourceController) but has **no GUI toggle**.
- **Job pruning?** Exists: `CleanupService.CleanupCompletedJobsAsync` deletes Succeeded/Cancelled
  jobs older than the retention window — default 3 days, env `COMPLETED_JOB_RETENTION_DAYS`
  (`Constants.cs:31`). NeedsAttention/Failed are kept forever (deliberate). Gap: not configurable
  from the GUI, no manual trigger button.
- **What does Komga/Kavita linking do?** It is a scan trigger only: after downloads, Kenku calls
  Komga `POST /api/v1/libraries/{id}/scan` (Kavita: `scan-multiple`) so the reader re-indexes the
  files Kenku wrote. No metadata sync. Worth saying in the settings UI.
- **Version in GUI?** Not exposed anywhere. Backend already embeds GitInfo + BuildInformation
  (logged at startup) — a `/v2/Version` endpoint + footer/settings display is cheap.
- **Language configurable from GUI?** Backend yes (`PATCH /v2/Settings/DownloadLanguage`), GUI no.
  Several other settings endpoints also lack GUI (UserAgent, compression, naming scheme, retry
  budget already partially covered).
- **More maintenance buttons?** Backend has 6 endpoints; GUI shows 2 (Clean database, Clean
  actions). Missing: CleanupOrphanedFiles (has dry-run), ResolveMissingVolumes,
  SyncChapterFileNames, ResetAndResolveVolumes. Could add: prune-completed-jobs now + retention
  setting.

## Feature takes (not started)

- **Connector/source management GUI** — near-term: a sources table in settings with
  enable/disable toggles (API exists). The "scraping DSL" idea is a much bigger bet: defining
  scrape logic in templates is building a scraper engine; revisit after the connector set
  stabilizes.
- **Built-in reader (drop Komga requirement)** — medium: an endpoint streaming pages out of a
  stored CBZ + a minimal reader page. Komga stays the power option.
- **Discovery** — cheapest first experiment: RSS "feed" page (manga/comic subreddits, GetComics
  feed). Tracker sync (AniList pull lists) is the structured version later.
- **Docs site** — agreed; lean quick-start + Docker getting-started, app theme, no slop.
- **OIDC auth / feature gates** — both future; single-user today.
- **AI ideas**: chapter→volume rollup upgrade fits the existing bundling machinery best;
  archive health check ties into Stage B (RAR/SharpCompress); scanlation scoring and variant
  covers are larger/lower value right now.

## Suggested order
1. Bug fixes the user approves from B1–B5 (B1/B2 first — they hit daily use).
2. Quick wins: version endpoint + display, language + maintenance buttons in settings,
   Komga explainer text, connector toggles.
3. Docs site. 4. Discovery experiment. 5. Reader.
