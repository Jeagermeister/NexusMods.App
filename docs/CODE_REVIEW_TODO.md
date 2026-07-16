# Code Review — Remaining Work

*Companion to [`CODE_REVIEW.md`](./CODE_REVIEW.md) and [`CODE_REVIEW_FIXES.md`](./CODE_REVIEW_FIXES.md).
Tiers 0–5 of the review roadmap are implemented (2026-07-14); this is what was **deliberately
deferred**, why, and what each item needs to proceed. Roughly in priority order.*

*Since this doc was written: migration `_0009` is registered (PR #74) and diff-based loadout
switching is implemented (PR #73) — both closed the items that used to head this list. Live
verification of #74 against real data also caught (and PR #75 fixed) a class of crash bug —
`LoadoutItem.Parent`/`.ParentId` throwing when accessed without a `HasParent()` guard on a
top-level, non-collection group — in 6 places across the app, not just the migration. Real
Thunderstore/mod.io thumbnails also landed as part of the layout epic (PR #70), closing what used
to be item 7 below.*

## Needs a decision or environment we didn't have

1. **CI dead lanes** (review #13) — 42 `RequiresNetworking` tests (16 NexusWebApi,
   26 CreationEngine, collection e2e, the one HttpDownloader test) never run in any lane; the
   suite projects coverage it doesn't deliver. Options: convert what can be hermetic (the
   `LocalHttpServer` helper exists and is unused — the HttpDownloader test is the natural first
   conversion), quarantine the rest behind an explicit category, or stand up a self-hosted lane
   with a `NEXUS_API_KEY`. Policy call + CI work.

2. **At-rest secrets → OS keyring** (review #15, `JWTToken.cs`) — the Nexus OAuth refresh token,
   API key, mod.io key, and Steam auth data are plaintext in the datastore/configs. Moving them
   behind Secret Service (Linux) / DPAPI (Windows) changes login storage and needs a migration +
   fallback story for headless boxes. Design doc first; a session of its own.

3. **Heroic/Legendary EGS locator** (review #17, second half) — Epic-via-Heroic games are still
   undetectable (no locator parses Legendary's `installed.json`). New feature; blocked on having
   an Epic-via-Heroic install to test against.

## Smaller follow-ups noted while implementing

4. **Wire `LinuxCompatabilityDataProvider` for Heroic installs** — `HeroicGOGGame.GetWinePrefix()`
   exists; wiring it through the locator would light up the `user.reg`/winhttp path (and REDmod
   deploy) for GOG/Heroic games, closing the "REDmod silently skipped on Linux for non-Steam
   stores" finding.
5. **Relocate the `IModSource` adapters out of App.UI** into their source layers — also the
   moment to fix the `Apocrypha.Library` → `App.UI` layering inversion the review flagged (§5).
6. **Nexus/Thunderstore download integrity — premise checked, not currently wireable as written**
   (review `se2`). This item assumed Nexus V2 exposes MD5 on files the way mod.io/collection
   externals do; checked against the vendored GraphQL schema, the official REST v1 client's
   TypeScript types, and Vortex's own source (2026-07-16): neither the GraphQL `ModFile` type nor
   the REST v1 `files.json` response carries a hash for a *known* file — Nexus's only hash surface
   is the reverse `fileHash(md5:)`/`fileHashes(md5s:)` lookup (supply a hash you already have, get
   matching files back), which is why the mod.io and Nexus-collection-external paths work (they
   get an md5 from a different source — the modfile/manifest metadata — not from `ModFile`
   itself) but regular Nexus mod downloads have nothing to verify against. Confirmed Vortex
   doesn't get one either — it hashes post-download client-side. Thunderstore is worse: no
   endpoint (v1, experimental) exposes a hash, and the backend doesn't even compute one for
   regular package zips (its SHA256 code paths belong to unrelated subsystems — schema-server
   blobs, r2modman profile sync). Real options if this is still wanted: (a) drop it — HTTPS
   already protects the transport and there's no additional integrity claim to check without an
   upstream-supplied hash; (b) hash post-download and cache it for `LibraryDuplicateFinder`/
   reverse-lookup purposes (matches what Vortex does for Nexus), which is a dedup feature, not
   integrity verification; (c) file a feature request upstream (a Nexus forum thread already
   exists asking to expose the hash on `ModFile`) and revisit once/if it ships. Not scoping further
   until Brian picks one.

   The one safe piece of this item — deduping the MD5-create/compute/compare/throw block that
   `ModIoDownloadJob` and `ExternalDownloadJob` both hand-rolled into a shared
   `Md5Hasher.VerifyAsync` helper — is done (PR #77).
7. **`IModSource` axaml enumeration** (review #9, increment 2) — the "get mods" flyout still
   declares one MenuItem per source. Parked deliberately: Avalonia templating of a mixed
   static/dynamic flyout costs more than the ~6 lines per future source it saves.

## Strategic (from the review body, unchanged)

8. **Event-sourced history retention** (§4) — nothing compacts the main store; undo depends on
   history, so this needs a retention policy, not blind compaction.
9. **Bethesda load-order readiness** (§6) — a CreationEngine `SortOrderVariety` owning
   `plugins.txt` (user-editable order, topo sort demoted to validation), plugin-header cache,
   `Ingest` implemented so LOOT/manual edits aren't clobbered.
10. **Per-source UI at scale** (§4 medium items) — eager TreeDataGrid root activation, search
    filter materialization on the UI thread, `FilterLoadoutItems` observing every mod page.

## Operational

11. **Hash-DB feed refresh** — the feed is live but still serves upstream's original data through
    the new channel. First refresh with Brian's own newly-scanned games: `steam login` →
    `steam app index` your owned games → build → release
    (runbook: `~/Source/Apocrypha-notes/HASHES-RUNBOOK.md`).
