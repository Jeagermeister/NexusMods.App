# Code Review — Remaining Work

*Companion to [`CODE_REVIEW.md`](./CODE_REVIEW.md) and [`CODE_REVIEW_FIXES.md`](./CODE_REVIEW_FIXES.md).
Tiers 0–5 of the original review roadmap are implemented (2026-07-14). A second, whole-app
consolidation review covering PRs #69–#92 ran 2026-07-28 (fixes landed as PRs #93–#96); this
doc remains the single ledger of what is **deliberately deferred**, why, and what each item
needs to proceed. Roughly in priority order. Finding ids (`B-1`, `C-1`, …) refer to the
2026-07-28 review's findings ledger (maintainer's notes).*

## Needs a decision or environment we didn't have

1. **CI dead lanes** — 42 `RequiresNetworking` tests (16 NexusWebApi, 26 CreationEngine,
   collection e2e, the one HttpDownloader test) never run in any lane. The 2026-07-28 review
   sharpened this: `Apocrypha.Collections.Tests` contains ZERO offline tests, so the
   collections install pipeline — the highest-churn subsystem — has no CI coverage at all.
   Options unchanged: convert what can be hermetic (the unused `LocalHttpServer` helper; the
   HttpDownloader test is the natural pilot), quarantine the rest behind an explicit
   category, or stand up a self-hosted lane with a `NEXUS_API_KEY`. Also note
   `AIsolatedGameTest` is CI-hostile for offline use (starts hosted services →
   protocol-handler registration shells out) — offline DB tests should use assembly-level
   `AGameTest<StubbedGame>` DI instead (the pattern PRs #92/#96 established).

2. **At-rest secrets → OS keyring** (`JWTToken.cs`) — Nexus OAuth refresh token, API key,
   mod.io key, and Steam auth data are plaintext in the datastore/configs. Needs a design
   doc + migration + headless fallback story; a session of its own.

3. **Heroic/Legendary EGS locator** — Epic-via-Heroic games are still undetectable (no
   locator parses Legendary's `installed.json`). Blocked on having an install to test
   against.

## Deferred from the 2026-07-28 review (largest first)

4. **Loadout-switch crash-window attribution (C-1)** — a mid-switch abort leaves disk
   part-target while `LastSyncedLoadout` still names the outgoing loadout; the next sync
   ingests the target's half-written files into the outgoing loadout's Overrides and
   reifies deletes for its files. Needs a switch-in-progress marker committed before
   `BuildProcessRun` mutates disk + a recovery path that diffs against the target without
   ingest attribution. The catastrophic-delete guard for the switch path landed in #94;
   this is the remaining (harder) half.

5. **`PluginsFile.Ingest` (B-1)** — still a no-op; with intrinsic sync rules that means an
   edited or pre-existing plugins.txt is permanently unmanaged: new installs never enter
   the file and seeded curated orders never reach disk. Minimal version: parse `*Name`
   lines → `ApplyCuratedOrder`. Pairs with a "reset to managed" affordance for the sticky
   intrinsic (the #90 gap). This is the last piece of the original strategic item 9 —
   everything else (SortOrderVariety owning plugins.txt, curated seeding, priority-Kahn)
   shipped in #92.

6. **Collection-install patch atomicity (S5-1)** — the standard-chain and FOMOD install
   branches self-commit the group, THEN apply curator patches, THEN tag
   `NexusCollectionItemLoadoutGroup` in a second tx. A patch failure strands an installed,
   deployed, unpatched, untagged group that `GetStatus` counts as installed — no retry
   heals it. Fix direction: patch before commit (`InstallReplicatedMod` already
   demonstrates the single-tx pattern) or a compensating retract. Related smaller
   deferrals: enabled-group aborts leave uncurated partial state (A-3); download rules
   committed in a detached second tx with no repair path (A-4); hash-mismatched curator
   patches deploy the unpatched original with only a warning (A-5).

7. **Sort-order creation race (B-3)** — `GetOrCreateSortOrderFor` is check-then-create;
   the seeder can race `SortOrderManager`'s CollectionGroup subscription into duplicate
   SortOrder entities, stranding the curated order in the shadowed one. Detect-after-commit
   or a tx-function uniqueness check. Watch live logs for "Multiple SortOrder entities"
   after collection installs to confirm incidence. Related: reconcile batching is
   per-commit (~1,800 reconciles across a 900-item install) and the seeder's 3-attempt CAS
   is likeliest to exhaust exactly then, dropping the curated order with only a LogError
   (B-8: debounce + surface seed failure into the job result); the raw CAS-retry path is
   still untested (B-9 — #96 covers the observable idempotency/convergence halves).

8. **MyGames fallback for xdg-less Linux (B-4)** — FO4/SSE silently fail to register when
   `KnownPath.MyGamesDirectory` cannot resolve (no `~/Documents`). Module-local
   `MyGamesOrFallback` helper, created lazily at first write; do NOT push the fallback into
   the shared FileSystem (Proton-prefix redirection must keep its semantics). Small
   standalone PR.

9. **Deploy/delete casing split (C-3)** — `DiskStateEntry` records the loadout-declared
   path while extraction writes through `CaseCanonicalizer`; deletion resolves literally,
   so on Linux a remapped file is orphaned on switch-away (re-opens the #88 inert-file
   class through the delete path). Record the resolved path + canonicalize delete targets;
   also `ActionWriteIntrinsics` bypasses the canonicalizer entirely (B-11), and ingest onto
   an existing `DeletedFile` creates a hybrid entity (C-4).

10. **Switch-path progress + cancellation (C-6)** — `ActivateLoadout`/`BuildProcessRun`
    drop the job and token: the 132GB A→B switch shows no progress and cannot be
    cancelled. Thread them through `ILoadoutManager`.

11. **RedMod full case-fold (S4-1 residual)** — #94 folded persistence matching and the
    SQL join, which stops the order-reset. Reactive keys still carry display casing
    (modlist.txt and MoveItems contracts require it), so cross-source key joins in the
    C# reconcile remain case-sensitive. The complete fix mirrors the Creation Engine's
    `PluginSortItemData` pattern: folded key + display-name field.

12. **ProcessLogs retention (M-4)** — `~/.local/state/Apocrypha/Logs/ProcessLogs/` grows
    unbounded (9k+ files observed). Startup sweep in `ProcessRunner` honoring a retention
    setting; fold in the `Runner.cs` "rework" TODO (settings-driven config) while there.

13. **19 upstream-legacy unguarded `LoadoutItem.Parent` sites (C-9)** — clustered in
    game diagnostic emitters (BG3, CP77, Bannerlord, SDV). Husk-triggered only; guard
    mechanically whenever those files are next touched. (All fork-new sites were fixed
    in #94.)

14. **Download-job cancellation handoff (F-2) — decision needed** — mod.io/Thunderstore
    `CreateDownloadJob` drop the caller's token after pre-flight; a CLI Ctrl+C does not
    stop the in-flight transfer. Confirm whether job-monitor-only cancellation is intended
    architecture; if yes, document the parameter, if no, link the tokens.

15. **Thunderstore persisted namespaces (F-6)** — models still use
    `NexusMods.Thunderstore.*` attribute ids (the mod.io twins were renamed). Persisted —
    requires a conscious schema-fingerprint re-accept; absorb into the next intentional
    schema-change PR, never a branding sweep.

## Smaller follow-ups (carried or new)

16. **Wire `LinuxCompatabilityDataProvider` for Heroic installs** — partially landed in
    PR #78; the remaining piece is the locator wiring that lights up `user.reg`/winhttp
    (and REDmod deploy) for GOG/Heroic games.
17. **Relocate the `IModSource` adapters out of App.UI** — #94 made CLI enumeration work
    (registrations moved to `AddApp`, classes public) and the 2026-07-28 review confirmed
    the adapters have zero UI coupling, so the eventual move is mechanical; the
    `Apocrypha.Library` → `App.UI` layering inversion remains the reason to do it.
18. **Nexus/Thunderstore download integrity** — unchanged: no upstream hash surface exists
    (verified against GraphQL schema, REST types, and Vortex source, 2026-07-16). Options
    still (a) drop, (b) post-download hash for dedup purposes, (c) upstream feature
    request. The shared `Md5Hasher.VerifyAsync` dedup landed long ago (PR #77).
19. **`IModSource` axaml enumeration** — parked deliberately (templating cost exceeds the
    ~6 lines per future source it saves).
20. **Nexus GraphQL client error handling** — thirteen `// TODO: handle errors` sites
    across `NexusModsLibrary`/`RunUpdateCheck`/`NexusApiClient` are one systemic theme:
    `AssertHasData()` throws raw on GraphQL errors (in two UI cases from inside a
    `Subscribe`). One design pass: typed error surface + toast for the UI paths.
21. **TODO themes worth issues when touched** (from the 108-TODO triage; keep with their
    subsystems): GOG Linux-installer indexing (unfinished verb chain), downloads-list
    persistence across restarts, FOMOD `IPluginDelegates`/`IContextDelegates` stubs,
    Bannerlord base-module ingestion hack + launcher-state write-back stub, collection
    manifest schema gaps (AdultContent/Summary/Author), REDmod winner-ranking heuristic,
    per-variety sort-direction persistence, settings discard-confirmation, actionable
    toasts, popout panels, localization of hardcoded section names.

## Strategic (unchanged)

22. **Event-sourced history retention** — nothing compacts the main store; undo depends on
    history, so this needs a retention policy, not blind compaction.
23. **Plugin-header cache (B-7)** — `PluginsFile.Write` and `MissingMasterEmitter` re-parse
    every plugin header per Apply/diagnostics pass. Measure on the real 682-plugin loadout
    first; if warranted, a `Hash → (ModKey, masters)` cache service consumed by both
    (content-addressed → immutable entries).
24. **Per-source UI at scale** — eager TreeDataGrid root activation, search filter
    materialization on the UI thread, `FilterLoadoutItems` observing every mod page.
    (The missing-`RefCount` triple-subscription in the rollup providers was fixed in #94.)

## Operational

25. **Hash-DB feed refresh** — the feed still serves upstream's original data; first
    refresh with locally scanned games per the runbook.
26. **API storm attribution** — the traffic monitor now reports per-window deltas and
    counts suppressed 429s (#94), exactly so the next big collection sync can name the
    storm. Read the log after that sync; the six raw `HttpClient` sites (image pipelines,
    markdown, TopBar, Steam session) remain uninstrumented blind spots if the storm turns
    out to be CDN-shaped (D-3).
