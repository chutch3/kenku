# CHANGELOG

<!-- version list -->

## v0.18.0 (2026-06-11)

### Bug Fixes

- Prefer the cached cover for tracked series so rotated source hosts cannot break covers
  ([`704be85`](https://github.com/chutch3/kenku/commit/704be85c3ded086c7db45d9a72d93ac634feea51))

- Re-resolve the series cover URL on chapter sync so rotated hosts heal
  ([`7f1af10`](https://github.com/chutch3/kenku/commit/7f1af10e18137e6246fe7b0c0a6bd6ba38c97db9))

- Report the stamped release version instead of empty git info in Docker builds
  ([`d842478`](https://github.com/chutch3/kenku/commit/d8424784e7deb0ce84d36a4d96ba50aea5b2c09e))

### Documentation

- Bring the READMEs up to date with comic sources, the job runtime, and current settings
  ([`73e278a`](https://github.com/chutch3/kenku/commit/73e278aa32591e7c3eae3bece61f69f620ad9f33))

### Features

- Explain layout behavior for comics in the layout select
  ([`a7a3200`](https://github.com/chutch3/kenku/commit/a7a3200b9488c33caaffbfcfbda57917444ae65e))

- Redesign series cards with a status-colored edge bar and a type-and-status info line
  ([`c2cb69d`](https://github.com/chutch3/kenku/commit/c2cb69de8f473c3fee61c07143c007617061f8bd))


## v0.17.0 (2026-06-11)

### Bug Fixes

- Hide manga volume assignment on comic series pages
  ([`0d0bffe`](https://github.com/chutch3/kenku/commit/0d0bffe8c5619dbf6c7a86b36ce0f4d377190dbb))

- Match connector names case-insensitively in the comic rule and torrent sweep, and pin the
  retention setting with a test
  ([`3e7bdef`](https://github.com/chutch3/kenku/commit/3e7bdef57e365bae5182497e75374acc8b1d3b48))

- Refresh series detail rollup and chapters, auto-refreshing when in-flight jobs drain
  ([`e64df86`](https://github.com/chutch3/kenku/commit/e64df86d89d05720494423e04e3e2a38dc333215))

- Skip MangaDex volume resolution for comic series
  ([`531b056`](https://github.com/chutch3/kenku/commit/531b056e0bcb3357d994c3dab147a0594356464a))

- Stop series delete from sweeping every chapter through the download client
  ([`2aaf7fc`](https://github.com/chutch3/kenku/commit/2aaf7fc8c2e3ceae12f963aeaa9dc3cd2465aa5f))

### Documentation

- Explain that library servers are rescan triggers, not metadata sync
  ([`e413c0c`](https://github.com/chutch3/kenku/commit/e413c0c6a0af9d63e07de2edd6087316d0971fbb))

### Features

- Badge comic series on cards and stop claiming indexer delivery for direct comic sources
  ([`411c7b3`](https://github.com/chutch3/kenku/commit/411c7b37e62c3c1064137944466f3ec84fb8729c))

- Configure job retention and download language from settings with full maintenance triggers
  ([`b527118`](https://github.com/chutch3/kenku/commit/b52711898a5b7315c4197dc2a2bf33c5fadce871))

- Confirm series deletion with in-flight feedback and a completion toast
  ([`b1f2e97`](https://github.com/chutch3/kenku/commit/b1f2e970f086b75ecf33238e178fc44ae951dd13))

- Enable or disable sources from settings
  ([`ae8a595`](https://github.com/chutch3/kenku/commit/ae8a595037ae310f59889a45556ae37b52d5e660))

- Expose the deployed version via the API and show it in the GUI
  ([`e790952`](https://github.com/chutch3/kenku/commit/e7909521eb58d1155393169fed9a69597f9f3620))

- Stable queue ordering with expandable job rows showing the full error
  ([`eb74163`](https://github.com/chutch3/kenku/commit/eb74163a9072b82c339993beb61df3f92208391c))

- Surface indexer rate-limit cooldowns in settings
  ([`cc84869`](https://github.com/chutch3/kenku/commit/cc848694ada1566e97b434860d4052a9384a5db8))

### Refactoring

- Surface delete failures, tick the cooldown badge, true queue footer counts, and registry-backed
  fetch keys
  ([`c72f700`](https://github.com/chutch3/kenku/commit/c72f7004d1e81c80845c31ff799cf50a2ac39148))


## v0.16.0 (2026-06-11)

### Continuous Integration

- Make version image builds reproducible from their tag and rerunnable on demand
  ([`eb33ce8`](https://github.com/chutch3/kenku/commit/eb33ce81538737bd1f28add0847463dafc9af27e))

### Features

- Add ComicHubFree as a page-reader comic source
  ([`f48e70a`](https://github.com/chutch3/kenku/commit/f48e70a948966868655343939ad11a96d4b4b937))

- Classify series as comics by source content type instead of acquisition kind
  ([`bf13f14`](https://github.com/chutch3/kenku/commit/bf13f14a145c27a977b8fb770e58a1d27ba63b0c))

- Discover GetComics series via tag archives and surface collection rows as chapters
  ([`c6ae823`](https://github.com/chutch3/kenku/commit/c6ae8236de9135acbdfdebd87b6c5a5f156c9d8b))

- Parse GetComics volume and collection titles into their series
  ([`3d3800b`](https://github.com/chutch3/kenku/commit/3d3800bc317663dcfc73db7c6bc6d75574c5cda9))


## v0.15.0 (2026-06-11)

### Bug Fixes

- End GetComics paging on a short page instead of probing into masked 404s
  ([`3318b3b`](https://github.com/chutch3/kenku/commit/3318b3bafb5efeb1d91870f45a467ab874ba9ad9))

### Features

- Add GetComics.org as a direct-archive comic source
  ([`67440f9`](https://github.com/chutch3/kenku/commit/67440f9f38ca11cdcd7638138f44ba00fffad897))

- Resolve GetComics posts to archive links at download time and park mirror-only posts for manual
  handling
  ([`03759a4`](https://github.com/chutch3/kenku/commit/03759a40a8bba84b284a19259ba5eade80f02ee5))

- Show direct-archive series as comics in the catalog
  ([`efc5c6e`](https://github.com/chutch3/kenku/commit/efc5c6e7503089c8931d349f3c67b88a2a2c5f4f))

### Testing

- Prove a GetComics direct-archive chapter downloads through the job runtime
  ([`47a9b35`](https://github.com/chutch3/kenku/commit/47a9b35a48ca18ba3184f5cda3e2cc4adc9ca54b))


## v0.14.0 (2026-06-10)

### Features

- Make the chapter-download retry budget configurable in download settings
  ([`2b79ee0`](https://github.com/chutch3/kenku/commit/2b79ee034e5726f94056db117a3b38985354947e))


## v0.13.2 (2026-06-10)

### Bug Fixes

- Move release-selection validation onto the record constructor params so the settings PATCH stops
  500ing
  ([`56fedd6`](https://github.com/chutch3/kenku/commit/56fedd6f5917ce9e439264481b688d862fedf1eb))


## v0.13.1 (2026-06-10)

### Bug Fixes

- Cool down a rate-limited indexer instead of researching it every chapter and burning the quota
  ([`ad5995a`](https://github.com/chutch3/kenku/commit/ad5995ab8fc6201459c68bb29b6b4e3b74b4e7e7))

- Read Metron credentials at request time so linking works without a restart
  ([`976e6c2`](https://github.com/chutch3/kenku/commit/976e6c23a04595274af426d08c44176191f49aea))

- Spare tracked series from the no-download cleanup so watchlist and paused series survive
  ([`2aecdd5`](https://github.com/chutch3/kenku/commit/2aecdd516824c121f78b6680e8bf7e75ca45f863))

- Sweep a deleted series jobs and tagged torrents so nothing retries against vanished rows
  ([`efb795a`](https://github.com/chutch3/kenku/commit/efb795a13886b7910bdd0b23bd8145480c26ef05))

### Chores

- Rename the backend test workflow to API Checks since it runs the full suite
  ([`04b92f6`](https://github.com/chutch3/kenku/commit/04b92f65bb0e13b333a28bcec1a5d34cef1996c5))

### Refactoring

- Collapse the repeated cover-and-sync enqueue blocks into one SeriesJobs helper
  ([`7e10ff7`](https://github.com/chutch3/kenku/commit/7e10ff70e2a78b15a77ae576162a10a11a81255a))

- Consolidate test fakes and image fixtures into shared FakeSeriesSource and TestImages
  ([`0ede957`](https://github.com/chutch3/kenku/commit/0ede957b34ebfb02c8d979bc823ce323e239b52a))

- Fold the eleven identical reconciler loops into one Reconciler base class
  ([`724ef7b`](https://github.com/chutch3/kenku/commit/724ef7bc25e7cd104fd46c71f0ccd2195e8abff7))

- Mirror schema field names on the SourceId DTO and map through From factories
  ([`8c1c5d7`](https://github.com/chutch3/kenku/commit/8c1c5d7b966ff5a895ac0a54ab7499a2b691abd2))

- Move the cover image pipeline off the SeriesSource base into CoverImageCache
  ([`81beb9b`](https://github.com/chutch3/kenku/commit/81beb9b2c40c8732c1f3b4738521a9c3dc80399a))

- Move the rollup aggregation and change-library workflow out of SeriesController into services
  ([`a5bda7e`](https://github.com/chutch3/kenku/commit/a5bda7ef8c9823e5b50588fef61d5984ef59ad49))

- Register each layer through extension methods and move boot work into StartupTasks
  ([`3b98e90`](https://github.com/chutch3/kenku/commit/3b98e9093cf208d7fb4debe4afcbd95cfe8911e9))

- Resolve domain services from the container instead of hand-constructing them in handlers
  ([`fe7c9b9`](https://github.com/chutch3/kenku/commit/fe7c9b968fd3d8202af031e43eb3a20749ee0b25))


## v0.13.0 (2026-06-09)

### Bug Fixes

- Coalesce duplicate enqueues onto NeedsAttention jobs so reconcilers stop spawning failed-job
  pileups
  ([`31a98fe`](https://github.com/chutch3/kenku/commit/31a98fe8e2a0088a062c3e272d679344cff64122))

- Make chapter-sync failures loud and strip the WeebCentral title slug that 404d chapter lists
  ([`60944fc`](https://github.com/chutch3/kenku/commit/60944fc690ecf7bc87ae3524562fdf71d3be4525))

- Treat torrent hand-off as deferred success so comic downloads stop failing and re-adding
  ([`6e339e5`](https://github.com/chutch3/kenku/commit/6e339e55b8cd66430c36e86ba97b9f2dbb16cd02))

### Features

- Acknowledge refresh actions with a brief toast so fast reloads are visible
  ([`4366575`](https://github.com/chutch3/kenku/commit/4366575daf9ec46a49e162c44897419f0a530e80))

- Add a per-series rollup so the library badge reflects real download state and failures
  ([`55d6c58`](https://github.com/chutch3/kenku/commit/55d6c58738e34399845b17f92dc8bc4385534211))

- Add Playwright golden-flow tests for the add modal and queue intervention
  ([`11a3a56`](https://github.com/chutch3/kenku/commit/11a3a56f54e7948429b9a52af5f41b9b6d2178f6))

- Add series from search via a modal with chapter preview and an add-only or add-and-download choice
  ([`5c8fb48`](https://github.com/chutch3/kenku/commit/5c8fb480a66aec31a14e74779167073e99bae0e8))

- Backfill missing covers from MyAnimeList and record why a cover refresh did nothing
  ([`be931ac`](https://github.com/chutch3/kenku/commit/be931ac8bea5c9b16fd8a21576b0da1612753566))

- Diverge the comic experience with source kinds, Metron covers for indexer series, and
  release-selection settings
  ([`ed1a494`](https://github.com/chutch3/kenku/commit/ed1a494fe44b445684445fe69e68a79aac3a9279))

- Turn the queue into a readable operations view with human job rows, contextual sync triggers, and
  source re-matching
  ([`53646f2`](https://github.com/chutch3/kenku/commit/53646f2eb92b7586514bc5147445527a2a16c0ee))


## v0.12.1 (2026-06-08)

### Bug Fixes

- Remove duplicate MetadataSourceController constructor that 500'd every endpoint
  ([`38cc244`](https://github.com/chutch3/kenku/commit/38cc244017f88a29d31895f9132354b2b6558fd6))

- Sync chapters immediately when a series is added so it is not left empty
  ([`420d2aa`](https://github.com/chutch3/kenku/commit/420d2aa391643592fe800f6a044f85c8d76f2df1))


## v0.12.0 (2026-06-08)

### Features

- Prune completed jobs after a retention window and bound the queue view
  ([`f683b51`](https://github.com/chutch3/kenku/commit/f683b51ca9928b39dbf8e70a1b08e4dcbbbc3e08))


## v0.11.0 (2026-06-08)

### Features

- Surface needs-attention jobs with dismiss and stop SendNotifications no-op churn
  ([`fd8d580`](https://github.com/chutch3/kenku/commit/fd8d5802441b48c3db65a95bb51cfff05414455e))


## v0.10.1 (2026-06-08)

### Bug Fixes

- Skip cover download for series with no cover URL to stop null-reference failures
  ([`80681d8`](https://github.com/chutch3/kenku/commit/80681d88acab6cbf5e228c75737fe97fef2700ef))


## v0.10.0 (2026-06-08)

### Features

- Show job run duration and timing in the queue view
  ([`9c16a3c`](https://github.com/chutch3/kenku/commit/9c16a3cd15f6a1aa4f93790c9f29bc29c5ef3d72))


## v0.9.3 (2026-06-08)

### Bug Fixes

- Reuse existing Author rows on metadata refresh to stop duplicate-key violations
  ([`e6bc970`](https://github.com/chutch3/kenku/commit/e6bc97027227737cb7922c0049a9dfedf519b66b))


## v0.9.2 (2026-06-08)

### Bug Fixes

- Default SeriesContext to split queries to stop cartesian-explosion timeouts
  ([`3056216`](https://github.com/chutch3/kenku/commit/30562160e9251354357c06306e5175eee9f55e44))


## v0.9.1 (2026-06-08)

### Bug Fixes

- Split MangaIncludeAll collection query to stop cartesian-explosion timeout
  ([`6cb81e1`](https://github.com/chutch3/kenku/commit/6cb81e1227604aec3a21bcbcbc2c60e10c2758c7))


## v0.9.0 (2026-06-07)

### Chores

- Remove rearch plan from gitignore
  ([`92e2427`](https://github.com/chutch3/kenku/commit/92e242751520a840c9559144b41c5bf42a5bf5d5))

- Stop tracking the rearch plan
  ([`a5c2028`](https://github.com/chutch3/kenku/commit/a5c202868ef2af4d83d609dba60d9483453ab92d))

### Features

- Migrate all background work to the job runtime
  ([`5ac33c2`](https://github.com/chutch3/kenku/commit/5ac33c2fb0cb7d0be79777030d60ee6432aeb078))


## v0.8.1 (2026-06-06)

### Bug Fixes

- Clarify the two metadata cards on series detail
  ([`3c29eb6`](https://github.com/chutch3/kenku/commit/3c29eb6fec7557319bcfa2b06b8559afc0e61b5d))

- Declutter source icons on series cards
  ([`92f6abd`](https://github.com/chutch3/kenku/commit/92f6abdc0c3758d1fc90de3c25e1dd80c9d8c7b1))

- Render primary elements in dark mode
  ([`e952642`](https://github.com/chutch3/kenku/commit/e9526424f0a75d24c9c329f0fd7bc95b8fb392cf))

### Documentation

- Add the release and deploy runbook
  ([`0fe312f`](https://github.com/chutch3/kenku/commit/0fe312f15d79edb39c2558ab9d80e1a897524d90))

- Capture activity and metadata concerns in the rearch plan
  ([`e52ce62`](https://github.com/chutch3/kenku/commit/e52ce62dec276b00ee1f8de8b28d2f8c13c6cd91))


## v0.8.0 (2026-06-05)

### Features

- Redesign the add-series search flow
  ([`ce4b3a5`](https://github.com/chutch3/kenku/commit/ce4b3a52600916e64e4d38174f227b7aea090677))

- Restructure settings into tabbed sections with status
  ([`c86094d`](https://github.com/chutch3/kenku/commit/c86094d05178e7fe838395be5de3f0ccc923feda))

- Streamline tracking with one-click add and clearer wording
  ([`d9aa84c`](https://github.com/chutch3/kenku/commit/d9aa84c8f9a074037da1ca976af6efb325ed1a41))

- Surface download progress and clearer source controls
  ([`0ffab48`](https://github.com/chutch3/kenku/commit/0ffab48a9ee118f7714e8d164286068a80fd57bd))


## v0.7.1 (2026-06-05)

### Bug Fixes

- Load page data on client-side navigation
  ([`6d8a76c`](https://github.com/chutch3/kenku/commit/6d8a76c623ba6f769ee93412a953cb0328717be7))

- Support enter-to-search and clarify source selection
  ([`4686bf0`](https://github.com/chutch3/kenku/commit/4686bf06686ebcfdccaee7c2921ea889d78dcf98))


## v0.7.0 (2026-06-05)

### Features

- Clarify series flow, surface status, and improve wayfinding
  ([`e8c9c05`](https://github.com/chutch3/kenku/commit/e8c9c0596e973ec389ffce67490ca026b8a43195))

- Reskin frontend with the karasu ink-and-vermillion theme
  ([`f8fd956`](https://github.com/chutch3/kenku/commit/f8fd956a500cec00f1f6092c8864f89f3214714c))


## v0.6.0 (2026-06-04)

### Bug Fixes

- Only capture entry-shaped tracker links, not generic site links
  ([`8e78043`](https://github.com/chutch3/kenku/commit/8e78043711268a2d409b0ece7e6cab2ee28677c1))

- Persist authoritative AniList id matches even without a volume aggregate
  ([`c5e9507`](https://github.com/chutch3/kenku/commit/c5e950703b4d0a8eb200d1c25f32d35d8ac84282))

- Tighten MangaUpdates link matching to entry urls
  ([`aed20ad`](https://github.com/chutch3/kenku/commit/aed20ad930b85fa197faa2992d7b8cae530028da))

### Features

- Backfill external tracker links onto existing series on re-import
  ([`8a6df18`](https://github.com/chutch3/kenku/commit/8a6df18d28243437061061789f2bd4d2550b195d))

- Capture external tracker links when parsing series pages
  ([`f2c1931`](https://github.com/chutch3/kenku/commit/f2c19311d4cde55c5f88054806841d153afd9b7f))

- Match volume source by AniList id and retire the blind title search
  ([`4dc1c49`](https://github.com/chutch3/kenku/commit/4dc1c49774a96f1c74c67ccbcd8339c549cf12ed))

### Refactoring

- Inject IHttpRequester into connectors instead of constructing it
  ([`8971fc7`](https://github.com/chutch3/kenku/commit/8971fc76015012678acf79bb37f7f96f98be3d31))

- Share an outside-in integration test base and fixtures
  ([`d0ec369`](https://github.com/chutch3/kenku/commit/d0ec3696fc35e88f5b6e0e39740f2c3a32710da5))

### Testing

- Add outside-in coverage for capture-to-match chain and link backfill
  ([`596b1c2`](https://github.com/chutch3/kenku/commit/596b1c226f0045471f9f914fe5e41fc31b90ae50))

- Cover connector link capture end-to-end through the add-series endpoint
  ([`bca9d5d`](https://github.com/chutch3/kenku/commit/bca9d5dfe13c7674c4d8dbdd38910980db695ff9))


## v0.5.0 (2026-06-04)

### Features

- Manual MangaDex link UI for series metadata
  ([`3fab14d`](https://github.com/chutch3/kenku/commit/3fab14d6130ce32efa308b3bb1f0a44c5ed952de))


## v0.4.3 (2026-06-04)

### Bug Fixes

- Resolve volumes for chapters before they download
  ([`2355500`](https://github.com/chutch3/kenku/commit/235550023460077842fae418acbc81af9ff1cdca))


## v0.4.2 (2026-06-04)

### Bug Fixes

- Prefer an indexer's own categories over the global comic filter
  ([`6964951`](https://github.com/chutch3/kenku/commit/69649511311c0bfa3a2604891e5a0b1d11ec86db))

### Testing

- Cover indexer search end-to-end across categories and failures
  ([`c681cd4`](https://github.com/chutch3/kenku/commit/c681cd43665bed1806c78ac0700074c4d72a914a))


## v0.4.1 (2026-06-03)

### Bug Fixes

- Send a User-Agent on MangaDex and Wikipedia requests
  ([`a26c116`](https://github.com/chutch3/kenku/commit/a26c116bd8e72059fa95d145b9770f89d146fc32))

### Testing

- Host the real app via WebApplicationFactory for true integration coverage
  ([`4448c03`](https://github.com/chutch3/kenku/commit/4448c031fdf21baec9a81ac2e56719c66e2cbde2))


## v0.4.0 (2026-06-03)

### Bug Fixes

- Honor manual volume lock during auto-match assignment
  ([`be0c2a3`](https://github.com/chutch3/kenku/commit/be0c2a3b7baee8892d702863b31199666d957015))

- Stop volume resolution rejecting valid MangaDex matches and fabricating oversized heuristic
  volumes
  ([`1a1a322`](https://github.com/chutch3/kenku/commit/1a1a3225ad3810f4407be44cf6ec5862066b2b86))

### Features

- Add Wikipedia chapter-list volume resolver
  ([`a1cc03e`](https://github.com/chutch3/kenku/commit/a1cc03ed6788e7f9fa695ad8fe1bc10c8d8063a0))

- Re-runnable multi-source volume resolution with manual lock
  ([`40333ed`](https://github.com/chutch3/kenku/commit/40333ede5a42650438985bb0ba031f47f2e49ae8))

- Rebuild bundled volumes when their chapter set changes
  ([`8ecf5de`](https://github.com/chutch3/kenku/commit/8ecf5decfbcc59da871c0d43a218427fc0fd04d7))

- Surface loose chapters with manual volume assignment in the UI
  ([`98def6c`](https://github.com/chutch3/kenku/commit/98def6caa74c2414fdaec67cc944692d56b5329a))

### Testing

- De-mock integration tests onto real units + WireMock, generalize naming
  ([`def4f7a`](https://github.com/chutch3/kenku/commit/def4f7a07a53ce9d9ccea792f5a7729be9ec2ae2))

- End-to-end pipeline integration test for merge, manual lock, self-heal
  ([`8166204`](https://github.com/chutch3/kenku/commit/8166204b3a8638b43aded2513fd333fc1398200c))

- End-to-end rebuild integration test with real zip I/O
  ([`f88c282`](https://github.com/chutch3/kenku/commit/f88c282d508197df6bd3a452a59ea1d0dc117621))

- Integration coverage for exact+heuristic composition and bloat-abort
  ([`7429b0d`](https://github.com/chutch3/kenku/commit/7429b0dc64f9eb993a60fdca9281c76ad27a2d52))

- Isolate Wikipedia nested-pipe test so an off-by-one count fails it
  ([`e7dea04`](https://github.com/chutch3/kenku/commit/e7dea0483e4045ddb007fc0360fbc2f7035713e0))

- Move controller and sync integration tests onto the harness and real queue
  ([`305dfc5`](https://github.com/chutch3/kenku/commit/305dfc54b1cdfda5f028059086755f1ab558601f))

- Pin frontend contract for loose-chapters volumes and assignment shapes
  ([`4abd35f`](https://github.com/chutch3/kenku/commit/4abd35f2d12ffdae15255ec9cc31a0a7ce774e8c))

- Rebuild ordering under the real WorkerQueue dependency scheduler
  ([`c6e1e36`](https://github.com/chutch3/kenku/commit/c6e1e36138bc77a46c17ecab082e4604b266eff9))

- VCR-style Dandadan integration test exercising real resolvers over captured HTTP
  ([`d35d013`](https://github.com/chutch3/kenku/commit/d35d0135a10a2dbdd73a32d0c3e94dfde15a995d))

- WireMock-based resolver integration with fault-injection cases
  ([`55058ed`](https://github.com/chutch3/kenku/commit/55058ed5ad277898b611a60ed11adb14bd52fb16))


## v0.3.2 (2026-06-03)

### Bug Fixes

- Place VolumeCBZ chapters flat instead of in volume folders
  ([`19c1968`](https://github.com/chutch3/kenku/commit/19c19683c9593bf4edcb4249754ffefb00b0a845))


## v0.3.1 (2026-06-03)

### Bug Fixes

- Treat bundled chapters as present so they are not re-downloaded
  ([`87d91c0`](https://github.com/chutch3/kenku/commit/87d91c02f78a527ed8b5b4e4921e4769d30e7f7b))


## v0.3.0 (2026-06-03)

### Bug Fixes

- Reconcile chapter files to the layout path after volume assignment
  ([`419255f`](https://github.com/chutch3/kenku/commit/419255f716deca868b0b72f898d5a5ccf57ebda2))

### Features

- Bundle ready volumes on a schedule, not only after a download
  ([`041dfa4`](https://github.com/chutch3/kenku/commit/041dfa41a5b9403830c0987fcb8c8a31700bb7d0))

### Refactoring

- Rename flow tests to integration tests
  ([`965309a`](https://github.com/chutch3/kenku/commit/965309a77b2234d5f6e8b48b033b6927cd917c40))


## v0.2.3 (2026-06-02)

### Bug Fixes

- Derive volume metadata on demand in the bundle endpoint
  ([`4429b9b`](https://github.com/chutch3/kenku/commit/4429b9b2d7ab6d10c2ab3ca0dd76eeb389ee38c9))


## v0.2.2 (2026-06-02)

### Bug Fixes

- Derive volume metadata from chapters when bundling instead of bailing
  ([`0b63598`](https://github.com/chutch3/kenku/commit/0b63598421c4ad65ff08417527784572071372c1))


## v0.2.1 (2026-06-02)

### Bug Fixes

- Load series sourceids in the chapter download worker query
  ([`227c903`](https://github.com/chutch3/kenku/commit/227c9032f6ebdbb670cf0e986e5a4e5b5c19bcf3))

### Testing

- Cover orphaned-file cleanup guard for untracked libraries
  ([`2af1693`](https://github.com/chutch3/kenku/commit/2af1693644c7f4eaffedce0e158ee707e20fdf11))

- Verify WeebCentral fetches the chapter images partial
  ([`6bc96f6`](https://github.com/chutch3/kenku/commit/6bc96f601d5ec544c773839d77110580b7d5d4ce))


## v0.2.0 (2026-06-02)

### Features

- Add library layout picker to the series download panel
  ([`bb35bb6`](https://github.com/chutch3/kenku/commit/bb35bb65e2df4dca427e60bbbbcec42223431200))

- Auto-bundle closed volumes under the VolumeCBZ layout
  ([`8a10214`](https://github.com/chutch3/kenku/commit/8a10214d69de19955dd609661745242bf1d9407b))

- Place downloaded chapters according to the series library layout
  ([`41e47d8`](https://github.com/chutch3/kenku/commit/41e47d83998e871fcb6b256de93bc02c0a4e1914))

- Surface per-volume bundle state and progress in the volumes API
  ([`4f909c8`](https://github.com/chutch3/kenku/commit/4f909c8bac8536240419a1efb63195dd3e41ab13))

### Refactoring

- Extract library layout path logic into LibraryLayoutResolver
  ([`0438043`](https://github.com/chutch3/kenku/commit/0438043b82fb978b2ed1371614395a69f8d4f1f1))


## v0.1.3 (2026-06-02)

### Bug Fixes

- Fetch WeebCentral chapter images from the images partial
  ([`d9cd2e9`](https://github.com/chutch3/kenku/commit/d9cd2e972dd87379b553dac59e09b366bfc6aec6))

- Prevent orphaned-file cleanup from deleting untracked libraries
  ([`0c79340`](https://github.com/chutch3/kenku/commit/0c79340d20951c43348970f1adf267b52960cd63))

- Skip writing an empty cbz when a chapter has no images
  ([`e4b7d1e`](https://github.com/chutch3/kenku/commit/e4b7d1e01c7de2241467483c327d1d8fb9315e32))


## v0.1.2 (2026-06-01)

### Bug Fixes

- Assign library to connector preview series via correct query param
  ([`ed23b37`](https://github.com/chutch3/kenku/commit/ed23b37c87d2953ac04144d7c5d188d396523962))

### Testing

- Guard frontend API calls against schema drift
  ([`7af1106`](https://github.com/chutch3/kenku/commit/7af110627aceae4395a39da022bb9414a15f084e))


## v0.1.1 (2026-06-01)

### Bug Fixes

- Load connector preview series detail from correct endpoint
  ([`26ebb40`](https://github.com/chutch3/kenku/commit/26ebb4040f35ce09ce951957663e69dc7b2b95c7))


## v0.1.0 (2026-06-01)

- Initial Release
