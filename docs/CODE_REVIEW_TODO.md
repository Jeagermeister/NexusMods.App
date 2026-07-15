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
6. **Nexus/Thunderstore download integrity** — mod.io downloads are now MD5-verified; the other
   sources still install unverified bytes (review `se2`). Thunderstore has no published hash in
   the per-package API; Nexus V2 exposes MD5 on files — wire it like the mod.io path.
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
