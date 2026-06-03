# CHANGELOG

<!-- version list -->

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
