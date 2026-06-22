# Comic path via GetComics (direct-download) — design & slice plan

> Status: **Stage A built** (slices 1–3 below; pack fan-out and Metron enrichment remain Stage B).
> See "Stage A findings" for the probe report. Grounds out the "Comic·Scrape" cell from
> [unified-content-design.md](unified-content-design.md). Supersedes the torrent-first comic attempts
> as the *primary* path; Comic·Torrent (Metron + indexers) stays as a fallback.

## Why this path

Verified 2026-06-10. The torrent path fails on structure: per-issue indexer search burns the daily
quota (51× 429 in an hour), and even at full quota only ~15 of Invincible's 144 issues had any
seeded single-issue release. GetComics inverts every one of those problems:

- **No quota.** Direct HTTP scrape of GetComics — no Prowlarr, no indexers, no seeders, no 429s, no
  cooldowns. The entire wall we hit today is absent.
- **Content is packs.** GetComics posts complete runs / collected editions / volume packs as single
  CBZ/CBR archives. That's how comics actually ship — the thing per-issue torrent search can't see.
- **Fits an acquirer we already have.** A GetComics post resolves to a download link to a finished
  archive → the existing **`DirectArchive`** acquirer kind (download a packaged .cbz, no
  page-stitching, no torrent client). DirectArchive is the simplest, most-proven acquisition path.
- **Cloudflare already handled.** GetComics is behind Cloudflare; the homelab already runs
  **FlareSolverr** (configured for WeebCentral). Same tool clears GetComics.
- **Ecosystem fit.** GetComics is what **Mylar3** automates, and kenku already presents itself to
  Prowlarr as Mylar. We're standing in this ecosystem already.

## How it maps onto the existing architecture

Nothing downstream of "add" changes. A GetComics comic becomes a normal tracked series whose chapters
(issues) carry source kind = DirectArchive, routed by the shared job engine like any other chapter.

```
Comic search (GetComics scrape)
   → add modal (pick library, preview issues/packs, Add only / Add & download)
      → Series + issues in catalog   [each issue: source kind = DirectArchive]
         → SHARED job engine (queue · retry · rollup)
            → DirectArchive acquirer  (download the archive link → .cbz)
               → SHARED tail (place · bundle into collected editions · notify)
                  → one library (/series/comics)  → one catalog UI
```

## Open design questions (decide during the slices)

1. **Metadata authority — Metron, GetComics, or both?**
   - *GetComics-only:* simplest; the post title/cover/list is the structure. Risk: messy titles,
     no canonical issue numbering, no "is this run complete" truth.
   - *Metron for structure + GetComics for files:* the clean arr model — Metron says "Invincible has
     144 issues + these collected editions", GetComics provides the archives, we match them. More
     work, far better library hygiene. **Leaning here, but start GetComics-only to prove the pipe.**
2. **What is a "chapter" for a comic?** An *issue*. A *collected edition / TPB* = a volume (the
   existing VolumeCBZ bundle layout already models "many chapters → one bound file").
3. **Packs / fan-out.** GetComics frequently posts a whole run as ONE archive. Two sub-questions:
   - Represent it as one volume item, or fan its contents out to N issue chapters?
   - The acquirer downloads one archive; **fan-out** (one archive → many issue .cbz, each marked
     Downloaded) is the one genuinely new mechanic — same as the torrent-pack case, so build once.
4. **Download-link resolution.** GetComics "Download" buttons are often an intermediate page or an
   external mirror (Mediafire, Pixeldrain, Mega…). DirectArchive currently expects a *direct* archive
   URL. The connector must resolve the post → a fetchable archive URL; mirrors that can't be automated
   (Mega/Terabox) are surfaced as "manual", not failed silently.
5. **HTML stability.** Scrapers break when sites re-theme (we just lived this with WeebCentral's
   chapter-list URL). Selector misses must be loud (throw → NeedsAttention with a reason), never a
   silent empty list — the §S2 lesson applies here verbatim.

## Slices (outside-in, TDD, one verified slice at a time)

> **Slice 0 — spike: DONE (2026-06-10).** Findings below — selectors grounded against the live site
> and the mature `csandman/get-comics` scraper. One material limitation surfaced (mirror-only posts).

### Spike findings (real structure)

**Site:** WordPress. **Search:** `https://getcomics.org/?s=<query>` (also `/page/N/?s=`).

**Search/index page → post URLs:** `h1.post-title a` (href = the post page). Each result also exposes
title, cover `<img>`, `Year :`, `Size :`, category, a description snippet.

**Post page → download links.** Three shapes (the scraper branches on them):
1. **Single comic** — one `a[title="Download Now" i]` (the GetComics-hosted "Main Server" link) +
   sibling mirror buttons.
2. **Multi-single** — several `a[title="Download Now" i]`, one per issue; each issue's title comes
   from `strong` in the preceding `<p>`, its host buttons follow in `.aio-button-center` containers.
3. **Multi-comic / mirror-only** — *no* "Download Now" main link; only external host buttons.

**Host buttons** are `<a>` with a `title` attribute naming the host, inside `.aio-button-center`:
`Download Now` (Main Server, GetComics-hosted), `Mirror Download`, `MEGA`, `Mediafire`, `Zippyshare`,
`DropAPK`, `Ufile`, `CloudMail`, `Userscloud`, `Terabox`, `Pixeldrain`. The Main Server link is a
GetComics redirect that 302s to the final archive file — **DirectArchive must follow redirects** (it
does a plain GET today; default HttpClient redirect-following covers it, verify).

**The material limitation (honest).** **Not every post is automatable.** Many — including the
Invincible *Compendium* checked live — are **mirror-only**: TERABOX / PIXELDRAIN / MEGA / WETRANSFER,
with no Main Server link. Mega, Terabox, WeTransfer can't be scripted; only the **Main Server
("Download Now")**, **Pixeldrain**, and **Mediafire** links are realistically automatable (and even
those need per-host resolvers — the reference scraper ships `utils/mediafire.ts`, `userscloud.ts`).
The scraper's own code carries the comment: *"assumes all single comic download pages have a main
server download link which is not always true."* So GetComics is **better than torrents (no quota,
packs native) but not a magic 100%-automatable source** — the connector must prefer automatable hosts
and surface mirror-only posts as "manual download," not fail them silently.

**Fixtures:** rather than commit copyrighted scraped HTML, the connector tests will use small synthetic
HTML matching these exact selectors (the pattern the existing WeebCentral/MangaDex connector tests
already use). The three post shapes above are the cases to cover.

1. **GetComics connector (search + list), Kind = DirectArchive.** A `SeriesSource` like the existing
   scrapers: `SearchManga` (scrape results), `GetMangaFromId/Url` (post → series + cover), `GetChapters`
   (post → issue/pack list with each item's resolvable archive URL in `WebsiteUrl`). Stubbed HTTP edge
   (the recorded fixtures), no network in tests. Routes through FlareSolverr like WeebCentral.
   *Net:* a GetComics series can be searched and added; its issues appear as chapters.
2. **DirectArchive download through the runtime, live-proven.** The DownloadChapter job acquires a
   GetComics issue via DirectArchive → .cbz on disk → Downloaded. This is the comic equivalent of the
   no-code gate, and it's *easier* than the torrent gate (synchronous download, no client/poll). Prove
   one issue end-to-end in prod.
3. **Link resolution + mirrors.** Resolve GetComics' intermediate/redirect download links to a real
   archive URL; non-automatable mirrors surface as a "needs manual download" outcome on the job row.
4. **Pack fan-out.** One downloaded run/collection archive populates many issue chapters (each placed,
   marked Downloaded, bundled). Shared with the torrent-pack case.
5. **(Optional) Metron enrichment.** Match the GetComics series to its Metron entry for canonical issue
   structure, covers, and "is the run complete" — turning the messy scrape into a clean library.

## Risks / notes

- **Dual-use, self-hosted.** Same posture as the existing manga scrapers and the *arr stack: this is a
  personal library manager; feasibility of a comic source is an engineering question, not an endorsement.
- **Fragility:** GetComics HTML changes will break selectors — make misses loud (throw, surface on the
  job), and keep recorded fixtures so a break is a failing test, not a silent empty library.
- **External mirrors** (Mega/Terabox/Rootz) can't be automated — design for "surface for manual", not
  silent failure.

## Stage A findings (2026-06-10, built — the §7 probe report)

Stage A is implemented (A1–A4 on `main`): GetComics connector (search + collapse + chapter list),
DirectArchive download proven through the booted-app runtime, lazy post→archive resolution with
mirror-only posts parked for manual handling, DirectArchive series shown as comics. Selectors were
grounded against the live site before coding (search page, a single post, a multi-section post, a
dead/mirror-only post). What the probe found:

### Naming reliability (Stage B risk #2)

Single current issues collapse cleanly and would be matchable cross-source:
`Invincible Universe – Battle Beast #9 (2026)` → series `Invincible Universe – Battle Beast`,
issue `9`, year `2026`. Within a series the numbering is consistent across posts.

Packs and collected editions are where it gets messy (real titles, real parser output):

| Post title | Collapses to | Issue |
|---|---|---|
| `Invincible Compendium Vol. 1 – 3 (2013-2019)` | `Invincible Compendium Vol. 1` (range read as issue) | `3` |
| `Invincible Iron Man Omnibus Vol. 2 (2024)` | `Invincible Iron Man Omnibus` (volume read as issue) | `2` |
| `Invincible Universe Compendium Vol. 1 (TPB) (2023)` | `Invincible Universe Compendium` | `1` |
| `Invincible #1 – 144 + Specials (2003-2018)` | whole title (no trailing number) | — (post skipped) |
| `Invincible Compendium (2025)` | `Invincible Compendium` | — (post skipped) |

So: omnibus/TPB runs become their own series with volume-as-issue numbering (workable, slightly
lying), range titles mis-collapse, and numberless posts are skipped by `GetChapters` (logged).
Cross-source matching against these titles (Stage B) is feasible for single issues but needs
range/volume-aware parsing for packs — `ReleaseTitleParser` as-is is not enough.

### Pack presentation (Stage B risk #3)

A post downloads as **one archive** (the Main Server `dls` link 302s straight to a single file,
e.g. `…/Invincible Universe - Battle Beast 009 (2026) (Digital) (Zone-Empire).cbr`). Packs are one
monolithic archive, not a zip of per-issue files — **not separable** without real pack-splitting.
Two honest caveats: the source file is often `.cbr` (RAR) but lands under kenku's `.cbz` naming
(readers generally cope; a container check/repack is a possible follow-up), and **multi-single
posts** (one `Download Now` per bundled issue, titles in the preceding `<p><strong>`) are parked
for manual handling in Stage A — fetching just the first link would silently drop the rest.
A single post can also carry several variants (HD/SD sections) with different hosts per section.

### Automatable coverage

Of the post pages inspected live: a current single issue had Main Server + Pixeldrain (automatable);
a TPB compendium had Main Server + Mediafire on one section, mirror-only (Terabox/Mega/Pixeldrain/
WeTransfer) on another; the old Invincible Compendium Vol. 1–3 post had **no download links left at
all** (only Read Online) — parked with a clear reason. Recent posts look mostly automatable (Main
Server is usually present); older posts decay toward mirror-only or dead. Mirror hosts seen today:
TERABOX, ROOTZ, VIKINGFILE, DATANODES, MEGA, WETRANSFER. The mirror buttons themselves are now
mostly `getcomics.org/dls/` wrappers (the Pixeldrain wrapper 302s to the share page; the connector
rewrites it to the direct `pixeldrain.com/api/file/{id}` URL).

**Verdict for Stage B:** GetComics alone covers current/ongoing series well; archival depth (old
runs, compendiums) is where it thins out. Metron + multi-source resolution would add real value for
back-catalogue completeness, but nothing in Stage A blocks on it.

### Stage A follow-up (2026-06-11): back-catalogue actually delivered

The first live attempt (Invincible, The Boys) confirmed the back-catalogue gap and produced fixes:

- **Title shapes** — "Series Vol. N – Subtitle" (TPB) and "Series #A – B + extras" (collection)
  now parse; volumes become volume chapters, collections are recognised rather than mangled.
- **Tag-archive discovery** — `GetChapters`/`GetMangaFromId` walk `/tag/{slug}/` first (complete,
  series-scoped) and fall back to search; the recency-ordered search lottery is gone. Note: the
  `IHttpRequester` seam masks non-success statuses as a synthetic 500, so paging treats any failure
  past page 1 (and a short page) as the end.
- **Collection rows as chapters** — a collection post's per-row "Main Server" links (the readable
  single-TPB/issue rows of the same series) become chapters, resolved lazily by row label; range
  rows (zips of archives) are skipped. Live: Invincible → 21 readable chapters, The Boys → 12 TPB
  volumes covering the full run.
- **ComicHubFree connector** — a second, page-reader comic source (`ImageList` kind): per-issue
  chapters built from the site's `/all` reading view (live: all 72 The Boys issues, real page
  downloads). Complements GetComics' archive quality with per-issue granularity.
- **ContentType** — sources now declare Manga|Comic independent of `AcquisitionKind`; the UI's
  comic detection uses it (an ImageList comic site is still a comic).

Still deliberately out: unpacking collection chunk zips (zip-of-cbr → per-issue files) — that is
the Stage B pack fan-out, and it would need RAR support (SharpCompress) the repo doesn't carry.

## Related near-term item (not part of this arc)

**Configurable retry attempts — DONE.** `KenkuSettings.DownloadMaxAttempts` (default 5) +
`GET/PATCH /v2/Settings/DownloadMaxAttempts`; the DownloadReconciler and the manual-download endpoint
stamp it on each `DownloadChapter` job. Settings → Downloads → Retry attempts. Outside-in, each change
red-first + mutation-verified.
