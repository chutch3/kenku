# Comics: verified state of the pipeline — 2026-06-10

Mandate: **verify what's broken and whether the intended flow can work, before any further comic
development.** Everything below was verified live against prod (0.13.0, deployed 23:38 UTC) or in
code, with the evidence cited. Symptom being explained: *"I have yet to find or download a comic
book."* — confirmed: `find /series/comics -name '*.cbz' | wc -l` → **0**, ever.

---

## Verified broken (the chain fails at every link)

**1. Metron metadata is dead until restart — creds snapshot at boot.**
`MetronClient` is a singleton built at startup from `settings.MetronUsername/Password`
(`ApplicationServiceCollectionExtensions.AddMetadataFetchers`). The user linked Metron at
**23:55:27** (settings.json mtime); the container booted at **23:38:50**. Result, live in the log:
`23:59:21 WARN MetronClient - Metron is not configured (missing username/password); skipping search`
— while the settings UI says "Connected" (it reads the file, not the client). Same stale-singleton
class as the ReleaseSelector bug fixed in R-wave; the fix is identical (read creds per call).
*Until a restart, comics have no metadata at all — no covers, no descriptions, nothing.*

**2. The comic "search/sync" model is a torrent-title parse, and it discards how comics actually ship.**
`IndexerBackedSeriesSource.GetChapters` = live indexer search; each release title is parsed for a
*single issue number*; anything else is dropped (`if (p.IssueNumber is null) continue;`).
Comics are predominantly distributed as **packs and collected editions** ("Invincible #1-144
Complete", "TPB Vol 1-25") — exactly the releases that parse to no single issue and get discarded.
Live evidence: Invincible sync at 23:59:15 → **"Got 1 chapters from connector"** (issue 106, of 144
published). The "chapter list" for a comic is whatever single-issue torrents happen to be seeded at
that moment, fetched under quota pressure. This is not a fixable parsing bug; it's the wrong model.

**3. Indexer quota burned, invisibly.**
**51 × HTTP 429** from The Pirate Bay / TorrentDownload (via Prowlarr) in under an hour — the daily
search quota was already consumed (shared with the other arr instance). Kenku's behaviour under 429:
no Retry-After handling, no per-indexer backoff, no user-visible signal — searches just silently
return nothing, which cascades into "Got 1 chapters" above. Per-issue download jobs each search again
(×5 retry attempts), multiplying the burn.

**4. Release selection rejected the one candidate, with defaults tuned for manga seeding levels.**
`23:59:19 INFO No torrent releases passed selection criteria for Invincible ch.106 (1 candidates)` —
min seeders 2 / cbz preference. Niche single-issue comic torrents often sit at 0–1 seeders. The knobs
are now in Settings → Release selection (S5), and the rejection reason does land on the job row — but
a user who hasn't internalised the queue won't connect "nothing downloaded" to "min seeders".

**5. No issue/volume structure.**
Comics have *issues* and *collected editions* (TPB/volumes). Today chapters = parsed release titles,
and volume mapping is a MangaDex/manga concept (correctly hidden for comics in S5b — but nothing
replaced it). Metron has the real structure (see feasibility).

**6. Series deletion leaves live residue** *(secondary, confirmed earlier)*: the deleted Invincible
left a NeedsAttention job retrying against a vanished SourceId and a tagged torrent still in
qBittorrent (24 torrents in the client vs 0 comic chapters in the DB — most stale from the pre-0.13
re-add loop). And `CleanupNoDownloadManga` ("Clean up database") deletes any all-sources-off series —
which now includes Add-only/watchlist and Paused series.

## Verified working

- **The download/finalize machinery** — hand-off → qBittorrent → poll → `FinalizeTorrent` moves the
  .cbz and marks Downloaded — is proven end-to-end *under test* (`TorrentDownloadEndToEndTests`), and
  the deferred-outcome fix (S1) holds in prod (no failed-job churn, no duplicate adds since deploy).
  It has simply **never been exercised live past release-selection**, because no release ever passed.
  The user's instinct is right: downloading can share the job engine as-is.
- **The manga path** end-to-end (I am a Hero: re-added 23:45, 22/22 chapters on disk by 23:55).

## Feasibility: can comics work the arr way? — Yes.

The arr model is *metadata-first*: the library is built from a metadata authority (Radarr↔TMDB),
and indexers are consulted only for releases. The comic equivalent exists and is already half-wired:

- **Metron API** (account already created): series search, per-series issue lists, issue numbers and
  cover dates, collected-edition info. Rate limit **20 req/min, 5,000/day** — generous, and ours
  alone, unlike the shared torrent-indexer quota. The existing `IMetronClient` seam already does
  series search + series detail; it needs an issue-list call added.
- **Releases**: indexers searched per *series/volume pack first* (one query), per-issue as fallback —
  a fraction of today's query volume, parsing pack/TPB titles instead of discarding them.
- **Download**: unchanged — the existing TorrentAcquirer → FinalizeTorrent loop.

### Target comic flow (proposal, not yet building)

1. **Search/add**: comic search hits **Metron**, not the indexers. The add modal shows the real
   series (publisher, year, issue count from Metron) — same modal shell, different source. Adding
   creates the series with its **full issue list from Metron** (chapters=issues, volumes=collected
   editions). Indexer quota: zero so far.
2. **Download planning**: per volume (or whole-run pack) search the indexers once; prefer packs and
   collected editions; per-issue only for gaps. Quota-aware: honour 429/Retry-After, back off
   per-indexer, surface "indexer rate-limited until ~HH:MM" on the job row.
3. **Acquire/finalize**: existing machinery, untouched. Packs finalise multiple issues from one
   torrent (FinalizeTorrent learns to fan a pack's files out to issues — the one genuinely new
   mechanic).

## The no-code live gate (do this before any building)

Tomorrow, after the indexer quota resets:
1. **Restart kenku** (loads the Metron creds saved at 23:55) — Metron metadata should immediately work.
2. Settings → Release selection → **min seeders 1**.
3. Re-add Invincible (Add & download).
Expected: ch.106's release is accepted → handed to qBittorrent → finalised into
`/series/comics/Invincible/` as a .cbz. That live-proves the entire acquire/finalize loop with zero
code changes, and isolates the remaining work to the search/metadata model above.

## Recommended order if the gate passes

1. Quick fixes (small, independent): Metron creds read live (no restart needed); delete-series sweeps
   its jobs + tagged torrents; `CleanupNoDownloadManga` spares tracked series; 429 → backoff +
   surfaced on the job row.
2. The model change (the real slice): Metron-first comic search/add + issue lists; pack-aware release
   search; pack fan-out in finalize. Outside-in per the dev context, one verified slice at a time.
