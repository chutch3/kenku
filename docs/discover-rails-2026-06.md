# Discover rails — expansion + settings integration (2026-06-22)

Flesh-out of "what other manga/comic discover rails can we add, and how do they fit settings?"
Grounded in the current code (exact refs). Shape: now → design → slices. *Revised 2026-06-22 after a
dev-context pass — denylist (not allowlist), MangaDex via the connector, providers own rails, slice 2
scoped; the rationale lives inline below.*

## Where rails come from today

Three distinct mechanisms feed the Discover page (`DiscoverController.cs`):

| Rail(s) | Source | Configurable? |
|---|---|---|
| Manga: Trending / Top-rated / New | **AniList**, fixed (`IAniListClient`, `DiscoverController.cs:34-57`) | no — always on |
| Manga: Genre rails | **AniList** per genre, **user-curated** via `DiscoveryGenres` (`DiscoverController.cs:62-72`; PATCH `/Settings/DiscoveryGenres` validated against the 18 `AniListGenres.All`) | **yes** — curated list |
| Comics: "Fresh releases" | fan-out over every connector implementing **`ILatestSeriesProvider`** — *only GetComics today* (`DiscoverController.cs:76-85`) | only via connector enable/disable |
| Feed: "Hot threads" | **reddit**, from `DiscoveryFeeds` subreddits (`DiscoverController.cs:93-104`, DB-backed, hourly refresh) | **read-only** — has a getter but **no PATCH/setter** (gap) |

Everything caches through `DiscoveryCache` (1h TTL, serves stale on failure). Entries are a flat
`DiscoveryEntry(Title, CoverUrl, Url, Source, Blurb)`. Two settings shapes already exist nearby — a
*curated allowlist* (`DiscoveryGenres`) and *connector enablement* (`DisabledConnectors`) — though, as the
model below explains, default-on rails actually need an inverted variant.

**The user's read is right:** every manga rail is AniList; comics is one GetComics rail.

## The rails worth adding

### Manga — MangaDex is the standout

MangaDex is already a wired-up connector (`Connectors/MangaDex.cs`, keyless public API, FlareSolverr
handled). Its `/manga` endpoint has ready-made browse orders → near-drop-in rails. **v1 = Popular +
Latest**, filtered to the configured `DownloadLanguage`, no content-rating filter (surface everything):
- `order[followedCount]=desc` → **Popular / Most-followed** *(v1)*
- `order[latestUploadedChapter]=desc` → **Latest updates** *(v1)*
- `order[createdAt]=desc` → **Recently added** *(deferred)*
- (later: tag-filtered rails to mirror genre rails, using MangaDex tags instead of the AniList 18)

Each `/manga` result already carries its tags — so we also **surface tags on each rail card** (decided):
add an optional `Tags` to `DiscoveryEntry`, populated by providers that have them (MangaDex tags, AniList
genres; GetComics/reddit leave it empty), rendered as chips on the card. Additive — its own slice.

**Why MangaDex rails are better than AniList ones, not just more:** a MangaDex entry carries a real
`mangadex.org/title/{id}` URL, so click→add resolves it **exactly** through the MangaDex connector's
by-URL path (`GetMangaFromUrl`). AniList entries have to be **fuzzy-matched by title** to some
connector — which is the exact mechanism behind the cover-swap bug we just fixed (F3, the
discover→connector cover mismatch). So MangaDex rails *diversify and* sidestep that whole bug class.

Lower-priority manga sources: **MyAnimeList/Jikan** (already integrated as a metadata fetcher — has
top/seasonal, but MAL URLs → fuzzy match, same caveat as AniList); **scrape connectors' own latest/popular
pages** (WeebCentral, Mangaworld, AsuraComic for manhwa — resolve cleanly by URL).

### Comics — cheap, the seam already exists

- **More GetComics rails** — it only surfaces its homepage as "Fresh releases" today, but GetComics has
  per-publisher (DC / Marvel / Image) and "weekly pack" pages. These reuse the existing scraper.
- **A 2nd comic connector implements `ILatestSeriesProvider`** (e.g. ComicHubFree) → it **auto-joins**
  the comics rail with *zero* controller changes. This is the cheapest possible add.
- **Metron-sourced** (integrated comics metadata) — recently-added/publisher browse; metadata-rich but
  resolves fuzzily to GetComics for the actual download.

## Fitting it into settings — the core question

Two gaps today: (1) the headline rails (Trending, Fresh comics, and any new ones) are **always on**,
not toggleable; (2) `DiscoveryFeeds` is **read-only** (no PATCH/UI) even though genres are editable.
A clean expansion must make new rails opt-in/out the way genres already are, without a sprawl of
one-off settings.

**Recommended model — providers own their rails; a denylist setting toggles them.**

- **One owner: `IDiscoveryRailProvider`** (generalize the existing `ILatestSeriesProvider`). A provider
  *declares* its rails — `Rails => [{ id, label, contentType }]` — and serves a rail's entries. The set
  of **available rails is the live aggregation** of every provider's declared rails (GetComics, MangaDex,
  the AniList shelves), not a separate static registry. Adding a rail = adding it in its provider,
  nowhere else — avoids the two-sources-of-truth drift (cf. the F4-A "single owner" lesson).
- **`KenkuSettings.DiscoveryRails: List<string>` = *disabled* rail ids (a denylist), default empty.**
  This is the key correction: rails are **default-on**, so a denylist (empty ⇒ everything on, and any
  rail we add *later* auto-appears) expresses that; an *enabled-list* (like `DiscoveryGenres`) is opt-in
  and would silently leave future rails **off** for anyone who'd customized. Rails (default-on, denylist)
  and genres (opt-in, allowlist) legitimately use **different list semantics** — so do *not* "mirror
  `DiscoveryGenres`" here.
- Setter + `Save()` **respecting the replace-on-load collection semantics** (`ObjectCreationHandling.Replace`,
  `KenkuSettings.cs:177-184`; there's a recent fix for default-collection merge-on-load — a new defaulted
  `List<string>` must follow it, with a load test).
- **`PATCH /Settings/DiscoveryRails`** (array of disabled ids, validated against the live rail set —
  drop unknowns) + expose `DiscoveryRails` in `SettingsResponse`.
- **Close the feeds gap on the way past:** add `SetDiscoveryFeeds` + `PATCH /Settings/DiscoveryFeeds` +
  a `DiscoveryFeedsField.vue` (mirror of `DiscoveryGenresField.vue`).
- **Genre rails stay the curated `DiscoveryGenres` allowlist** — different policy (opt-in), kept separate.

**Frontend — data-driven, but scoped:**
- **`GET /v2/Discover/Rails`** returns the **enabled simple provider rails** (live set − denylist) as
  `[{ id, label, contentType, entries }]`; `discover.vue` renders *those* generically (split Manga /
  Comic). New simple rails become backend-only.
- **Genre rails and the reddit feed stay their own bespoke sections** — they carry real, tested logic
  (genre rails self-fetch + `mangaSeen` dedup + `genresHaveContent`; the feed is DB-backed with
  `feedStarved`). Do **not** force-fit that into the generic shape; the data-driven endpoint covers only
  the flat provider rails (trending / top-rated / new / popular / latest / comics).

The Discovery settings tab then shows: a **grouped on/off list of rails** (Manga / Comics) from the live
provider set, plus the existing genres field and a new feeds field.

## Suggested slices (test-first, per the dev workflow)

1. **Rail-provider seam + `DiscoveryRails` denylist setting.** Generalize `ILatestSeriesProvider` →
   `IDiscoveryRailProvider` (declares `Rails` + serves entries per rail id); existing sources declare
   their current rails (GetComics → `comics-fresh`; an AniList shelf provider → trending / top-rated /
   new). Add `KenkuSettings.DiscoveryRails` (disabled ids, default empty) + setter + `PATCH` +
   `SettingsResponse` field. **Behaviour unchanged** (nothing disabled). Tests: live set − denylist;
   PATCH round-trip; **settings load uses replace-not-merge** for the new collection. *(backbone — no-op;
   its "verified" gate is these unit/integration tests, not a real run)*
2. **`GET /v2/Discover/Rails`** returns the enabled *simple provider rails* `[{id,label,contentType,entries}]`;
   `discover.vue` renders those generically. **Genre rails + reddit feed stay their existing sections**
   (their `discover.test.ts` coverage stays green). Test: a disabled id is omitted (one mocked endpoint).
3. **MangaDex Popular + Latest — through the connector.** MangaDex implements `IDiscoveryRailProvider`,
   **reusing its existing `IHttpRequester` + cover-URL building + models** (no parallel client); rails hit
   `order[followedCount]` / `order[latestUploadedChapter]`, **filtered to `DownloadLanguage`**
   (`availableTranslatedLanguage` / `hasAvailableChapters`), **no content-rating filter**; build
   `DiscoveryEntry` with `mangadex.org/title/{id}` URLs. Test the provider with a **mocked `IHttpRequester`**
   (owned seam, per Circle Architecture) — assert the language filter is sent + that an entry resolves
   by-URL through the MangaDex connector. *(highest value)*
4. **GetComics rails = Fresh + Weekly** — GetComics declares those two via the same seam (per-publisher
   deferred). *(cheap, reuses the scraper)*
5. **Tags on rail cards** — add optional `Tags` to `DiscoveryEntry`; MangaDex populates from its tag
   relationships, AniList from genres, others empty; `DiscoveryRail` card renders them as chips. Additive;
   note it's a shared-contract change (all providers see the new field). *(the surface-tags ask)*
6. **Feeds editable** — `DiscoveryFeeds` setter + PATCH + `DiscoveryFeedsField.vue`. *(closes the gap)*
7. **(optional) a 2nd comic connector implements `IDiscoveryRailProvider`** — auto-joins the comics rails.

**Fixed rail order** (in the provider declarations, so the page is deterministic): manga — Trending →
Popular → Latest → New → Top-rated → genre rails; comics — Fresh → Weekly.

Caching/rate-limits: every rail goes through `DiscoveryCache` (1h TTL), and MangaDex rails reuse the
connector's rate-limited requester — so source rate limits aren't a concern at the rail level.

## Acceptance & regression (the done-bar for each slice)

A slice is done only when all three hold: **(a)** its new test went Red→Green; **(b)** the named
*existing* tests stay green (the don't-break guard); **(c)** the real-run check passes (or it's an
explicit no-op). The guards below already exist.

| Slice | (a) New test (Red→Green) | (b) Regression guard — stays green | (c) Real-run check |
|---|---|---|---|
| 1 seam + denylist | denylist filters the live set; PATCH round-trips; **settings load = replace-not-merge** | `DiscoverControllerTests`, `DiscoveryCacheTests`, `SettingsControllerTests`/`SettingsEndpointTests` | none — no-op; gate is unit/integration only |
| 2 `/Discover/Rails` + generic FE | disabled id omitted; endpoint shape | **`discover.test.ts` (11), `discover-genres` e2e, `DiscoverControllerTests`, `api-contract`** | discover renders the same rails / order / dedup / empty-states |
| 3 MangaDex | provider (asserts the language filter is sent; entry resolves by-URL) | MangaDex **connector** search/add tests unchanged | Popular/Latest show; click→add resolves + the cover sticks |
| 4 GetComics Fresh+Weekly | Weekly rail | the existing Fresh-comics test | both rails show |
| 5 tags | chips render; empty ⇒ nothing | **every `DiscoveryEntry` construction compiles**, `discover.test.ts`, `api-contract` | tags on MangaDex/AniList cards; others bare |
| 6 feeds editable | PATCH round-trip | `DiscoveryFeedEndToEndTests`, `SettingsEndpointTests` | edit feeds → rail updates |

**Slice 2 is the regression-risk slice — three explicit don't-breaks:**
1. **Decide where dedup lives.** Today `discover.vue` dedups across the flat manga rails (`mangaSeen`) and
   the genre rails *exclude* seen titles. Moving the flat rails to `/Discover/Rails` while genre rails stay
   bespoke means the **rails endpoint must dedup the flat set and expose the seen-titles** so genre rails
   keep excluding them. Unowned dedup silently breaks the "no repeated title" guard (`discover.test.ts`).
2. **Characterize the page in e2e *before* the refactor.** Collapsing many per-rail mocks to one
   `/Discover/Rails` mock means `discover.test.ts` gets rewritten — which can quietly drop the
   dedup/order/empty-state assertions. Front-load a Playwright spec asserting the real rendered rails,
   their order, dedup, and the empty-state + `feedStarved` copy, so the refactor is held to actual output.
3. **Dispose of the old endpoints.** Once the FE reads `/Discover/Rails`, `/Discover/Manga`, `/Manga/New`,
   `/Manga/TopRated` are unused — **delete them** (after confirming nothing else calls them) rather than
   leave dead routes.

**Cross-cutting mechanics:**
- **OpenAPI regen + `api-contract` green on every DTO/endpoint change** (slices 1 settings field, 2 new
  endpoint, 5 `Tags`) — regenerate `API_v2.json` (mind the `APP_DATA` write gotcha) or the FE types drift.
- **`DiscoveryEntry.Tags` is an optional, defaulted last param** so the record change doesn't touch every
  provider's construction — existing entries stay empty and existing rails render unchanged.
- **CI (incl. e2e) is the final per-slice gate**, and the discover e2e is where a discover-page regression
  surfaces — which is exactly why slice 2 needs the characterization spec first.

## Decisions (resolved with user, 2026-06-22)

Forks (mechanism is in the model/slices above, not repeated here):
- **New rails default ON** — via the denylist.
- **Frontend = data-driven `GET /v2/Discover/Rails`**, scoped to the flat provider rails.

Build scope:
- **MangaDex v1 = Popular + Latest**, **filtered to `DownloadLanguage`**, **no content-rating filter**
  (surface everything — a rating filter can become a setting later if it ever bites).
- **GetComics = Fresh + Weekly** (per-publisher deferred).
- **Fixed rail order** (see slices).
- **Surface tags on each rail card** — `DiscoveryEntry.Tags`, additive slice.

### Still open (lower priority, not blocking)

- **MangaDex genre/tag rails:** MangaDex tags are a richer vocabulary than the AniList 18 — worth a
  follow-up to let genre rails come from MangaDex too, or keep genre rails AniList-only for now? Park
  until the MangaDex Popular/Latest rails are in.
- **Other sources from "rails worth adding"** (MyAnimeList/Jikan, Metron comics browse, scrape
  connectors' latest/popular pages, MangaDex "Recently added") — each becomes an `IDiscoveryRailProvider`
  once the seam lands; park until Popular/Latest + GetComics categories prove the pattern.
