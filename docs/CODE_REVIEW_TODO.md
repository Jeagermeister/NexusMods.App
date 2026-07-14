# Code Review — Remaining Work

*Companion to [`CODE_REVIEW.md`](./CODE_REVIEW.md) and [`CODE_REVIEW_FIXES.md`](./CODE_REVIEW_FIXES.md).
Tiers 0–5 of the review roadmap are implemented (2026-07-14); this is what was **deliberately
deferred**, why, and what each item needs to proceed. Roughly in priority order.*

## Needs a decision or environment we didn't have

1. **Register migration `_0009`** (`SchemaVersions/Services.cs`) — the conflict-priority backfill
   is fixed but unregistered. Migrations run against real user DBs at startup, so it stays off
   until a legacy-DB test exists: add a Migration-8 snapshot beside the other
   `MigrationSpecificTests`, assert every `LoadoutItemGroup` gets a sequential priority, then
   append `.AddMigration<_0009_AddLoadoutItemGroupPriority>()`.

2. **Diff-based loadout switching** (review #11 part 2, `ALoadoutSynchronizer` ~`:1026`) —
   switching still resets to vanilla then re-extracts everything instead of diffing the two
   loadout trees. Big win for multi-loadout users, but it's a sync-core rewrite in the
   anti-data-loss zone: needs real-game apply testing (manage → switch → verify files), not just
   suites. Do it in a session with the game rig available.

3. **CI dead lanes** (review #13) — 42 `RequiresNetworking` tests (16 NexusWebApi,
   26 CreationEngine, collection e2e, the one HttpDownloader test) never run in any lane; the
   suite projects coverage it doesn't deliver. Options: convert what can be hermetic (the
   `LocalHttpServer` helper exists and is unused — the HttpDownloader test is the natural first
   conversion), quarantine the rest behind an explicit category, or stand up a self-hosted lane
   with a `NEXUS_API_KEY`. Policy call + CI work.

4. **At-rest secrets → OS keyring** (review #15, `JWTToken.cs`) — the Nexus OAuth refresh token,
   API key, mod.io key, and Steam auth data are plaintext in the datastore/configs. Moving them
   behind Secret Service (Linux) / DPAPI (Windows) changes login storage and needs a migration +
   fallback story for headless boxes. Design doc first; a session of its own.

5. **Heroic/Legendary EGS locator** (review #17, second half) — Epic-via-Heroic games are still
   undetectable (no locator parses Legendary's `installed.json`). New feature; blocked on having
   an Epic-via-Heroic install to test against.

## Smaller follow-ups noted while implementing

6. **Wire `LinuxCompatabilityDataProvider` for Heroic installs** — `HeroicGOGGame.GetWinePrefix()`
   exists; wiring it through the locator would light up the `user.reg`/winhttp path (and REDmod
   deploy) for GOG/Heroic games, closing the "REDmod silently skipped on Linux for non-Steam
   stores" finding.
7. **Real Thunderstore/mod.io thumbnails** — both sources still show the fallback icon everywhere
   (Library + Downloads). Needs an icon resource pipeline fed from
   `ThunderstorePackageMetadata.IconUri` / mod.io logo URLs; the Nexus pipeline is the pattern.
8. **Relocate the `IModSource` adapters out of App.UI** into their source layers — also the
   moment to fix the `Apocrypha.Library` → `App.UI` layering inversion the review flagged (§5).
9. **Nexus/Thunderstore download integrity** — mod.io downloads are now MD5-verified; the other
   sources still install unverified bytes (review `se2`). Thunderstore has no published hash in
   the per-package API; Nexus V2 exposes MD5 on files — wire it like the mod.io path.
10. **`IModSource` axaml enumeration** (review #9, increment 2) — the "get mods" flyout still
    declares one MenuItem per source. Parked deliberately: Avalonia templating of a mixed
    static/dynamic flyout costs more than the ~6 lines per future source it saves.

## Strategic (from the review body, unchanged)

11. **Event-sourced history retention** (§4) — nothing compacts the main store; undo depends on
    history, so this needs a retention policy, not blind compaction.
12. **Bethesda load-order readiness** (§6) — a CreationEngine `SortOrderVariety` owning
    `plugins.txt` (user-editable order, topo sort demoted to validation), plugin-header cache,
    `Ingest` implemented so LOOT/manual edits aren't clobbered.
13. **Per-source UI at scale** (§4 medium items) — eager TreeDataGrid root activation, search
    filter materialization on the UI thread, `FilterLoadoutItems` observing every mod page.

## Operational

14. **Hash-DB feed refresh** — the feed is live but serves the upstream 2025-09-30 data. First
    real refresh: `steam login` → `steam app index` your owned games → build → release
    (runbook: `~/Source/Apocrypha-notes/HASHES-RUNBOOK.md`).
