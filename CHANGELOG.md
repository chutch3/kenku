# CHANGELOG

<!-- version list -->

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
