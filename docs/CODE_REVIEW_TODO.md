# Code Review — Remaining Work

*Companion to [`CODE_REVIEW.md`](./CODE_REVIEW.md) and [`CODE_REVIEW_FIXES.md`](./CODE_REVIEW_FIXES.md).
Tiers 0–5 of the original review roadmap are implemented (2026-07-14). A second, whole-app
consolidation review covering PRs #69–#92 ran 2026-07-28 (fixes landed as PRs #93–#96); this
doc remains the single ledger of what is **deliberately deferred**, why, and what each item
needs to proceed. Roughly in priority order. Finding ids (`B-1`, `C-1`, …) refer to the
2026-07-28 review's findings ledger (maintainer's notes).*

## Needs a decision or environment we didn't have

1. **CI dead lanes** — **the pilot is done; the remainder is not convertible and needs a
   decision.** The HttpDownloader test was converted onto `LocalHttpServer` and now runs in
   every lane, taking `HttpDownloadJob` from zero CI coverage to two hermetic tests. That was
   the only cheaply-convertible lane, and the conversion established the pattern.

   The other ~40 `RequiresNetworking` tests **cannot** follow it as written: the 26
   CreationEngine and 16 NexusWebApi tests download real mods, and `CollectionInstallTests`
   additionally needs a **premium** Nexus account, snapshots recorded against live revisions,
   and `ACyberpunkIsolatedGameTest`, which is CI-hostile anyway (starts hosted services →
   protocol-handler registration shells out). Making them hermetic means faking the Nexus
   GraphQL API plus the CDN — a fixture far larger than the tests.

   `Apocrypha.Collections.Tests` still has ZERO offline tests **in that assembly**, but the
   collection status logic it exercises is now covered offline from
   `Apocrypha.DataModel.Tests` (`OfflineCollectionStatusTests`) — and via a cheaper route than
   the "synthetic collection archive" this item used to recommend: `AArchivedDatabaseTest`
   already provides a **recorded datastore containing a real installed collection**
   (`two_sdv_collections_added_removed.zip` — g14kxi revision 42, 110 downloads, 96 required),
   fully offline with a substituted GraphQL client. Prefer that harness for anything that needs
   realistic collection shape; keep synthetic construction for edge states the recording does
   not contain (that test mutates the recording to build one — see item 6).

   Still uncovered offline: the install *job* itself (`InstallCollectionJob`), which needs the
   archive-parsing and download entities a recording alone does not supply. The standing
   alternatives for the network-bound remainder: quarantine behind an explicit category, or a
   self-hosted lane with a `NEXUS_API_KEY`.

2. **At-rest secrets → OS keyring** (`JWTToken.cs`) — Nexus OAuth refresh token, API key,
   mod.io key, and Steam auth data are plaintext in the datastore/configs. Needs a design
   doc + migration + headless fallback story; a session of its own.

3. **Heroic/Legendary EGS locator** — Epic-via-Heroic games are still undetectable (no
   locator parses Legendary's `installed.json`). Blocked on having an install to test
   against.

## Deferred from the 2026-07-28 review (largest first)

4. ~~**Loadout-switch crash-window attribution (C-1)**~~ — **FIXED.**
   `GameInstallMetadata.SwitchInProgressLoadout` is committed before `BuildProcessRun` touches
   disk and retracted in the same transaction that sets `LastSyncedLoadout`, so it is set
   exactly while disk and the database disagree. `Synchronize` now converges to the marked
   loadout through `BuildProcessRun` (which never ingests) before anything is allowed to read
   disk as user intent. The corruption was reproduced first and the fix verified against it:
   `InterruptedSwitchRecoveryTests` fails without the recovery call, with loadout B's file
   adopted into loadout A. The catastrophic-delete guard for the switch path landed in #94.

   Residual, deliberately not covered: a crash during a *same-loadout* Apply can still reify a
   not-yet-committed delete as a user deletion on the next sync. It is a much narrower window
   and does not cross loadouts, so it is not part of C-1; it belongs with item 10's
   progress/cancellation work on that path.

5. **`PluginsFile.Ingest` (B-1)** — **the ingest half is implemented.** A hand-edited or
   pre-existing plugins.txt is now parsed (`*Name` and bare lines; comments, blank lines and
   non-plugin lines ignored; duplicates folded case-insensitively keeping the first casing) and
   persisted through `ApplyCuratedOrder`, so the order is learned instead of discarded. Only the
   order is taken — the `*` enabled-flag is not, because enablement lives on loadout items.

   **Not verified end-to-end, so do not read this as finished:** the action mapping routes an
   edited file to `AdaptLoadout` (ingest) and an unchanged one to `WriteIntrinsic` (regenerate),
   so in principle a learned order is written back on the following Apply. Whether that actually
   converges depends on disk-state bookkeeping for intrinsic files, which nothing covers — there
   is no end-to-end intrinsic harness, because one needs a real Creation Engine install and
   `AIsolatedGameTest` is CI-hostile. So the "reset to managed" affordance for the sticky
   intrinsic (the #90 gap) **stays open**, and the write-back leg wants confirming on the real
   FO4 loadout.

6. ~~**Collection-install patch atomicity (S5-1)**~~ — **FIXED, both halves.** The create half
   below landed first (compensating retract); the detect half is now closed too — see the end of
   this item.

   The standard-chain and FOMOD install branches self-commit the group, THEN apply curator
   patches, THEN tag `NexusCollectionItemLoadoutGroup` in a second tx. A patch failure stranded
   an installed, deployed, unpatched, untagged group that `GetStatus` counts as installed — and
   because the job skips anything reporting installed, no retry healed it.

   **Fixed by the compensating-retract route** (of the two directions this item offered).
   Patch-before-commit was ruled out for these two branches: patch keys resolve against the
   *installed* layout, and that layout is only queryable once the group is committed — which is
   precisely why they install first. `InstallCollectionDownloadJob` now wraps both post-commit
   regions (`PatchInstalledGroupOrRetract`, and the tagging transaction in `StartAsync`) so any
   failure removes the group via `CollectionDownloader.RetractStrandedItemGroup`, returning the
   download to "in library" — a state a retry *can* heal. The retract is recursive (files go
   with the group, or the retry installs over half-installed remains) and never throws, so it
   cannot mask the original failure. Covered offline by `OfflineCollectionStatusTests`
   `RetractingAStrandedGroupRestoresARetryableState_S5_1` and
   `RetractingAStrandedGroupRemovesItsFiles_S5_1`. Note the other two branches were already
   atomic — `InstallReplicatedMod`, `InstallBundledMod` and `InstallFomodWithPredefinedChoices`
   all mint the `NexusCollectionItemLoadoutGroup` tag inside the install transaction; only the
   standard chain (`LoadoutManager.InstallItem`, which commits internally) tags in the second tx.

   **The detect half is now FIXED too.** `GetStatus` requires the collection-item tag before it
   will call a group installed, so a group stranded by a crash between the install commit and the
   retract reports in-library — a state the retry does not skip. The migration got the tag-blind
   path this needed: `GetStatusIgnoringCollectionItemTag` (`internal`, `InternalsVisibleTo`
   `Apocrypha.DataModel.SchemaVersions`), called only from `_0002_NexusCollectionItem`.

   Two things that were not obvious going in, and that any future change here has to respect:

   - **The tag check is "either attribute", not `Download`.** `_0002_`'s fallback branch backfills
     `IsRequired` alone for pre-tag items it cannot match to a download, so a genuine legacy install
     can carry `IsRequired` with no `Download`. Requiring the full tag would report every one of
     those users' installed mods as merely in-library. A crash-stranded group carries *neither*
     attribute, which separates the two cases exactly (`CollectionDownloader.HasCollectionItemTag`).
   - **No recorded datastore can cover the migration.** Every committed legacy snapshot has zero
     `NexusCollectionLoadoutGroup` entities (probed 2026-08-01 across all seven; the `Collections`
     count in their verified stats is `CollectionGroup`, i.e. the user's own collection, not a Nexus
     one), so "re-verify against a pre-`_0002_` recorded datastore" is not achievable as written.
     `TheMigrationStillTagsEveryItemAfterTheStatusChange_S5_1` synthesises the pre-tag state from the
     `AArchivedDatabaseTest` recording instead — strip the tags off a real installed collection, drive
     `_0002_` directly, expect every item re-tagged — and it was confirmed to fail (orphaning items
     into the fallback branch) when the migration is pointed back at tag-aware status.

   Detection alone would only have converted "permanently stranded" into "a second group installed
   beside the remains", so `InstallCollectionJob` now sweeps first: `RetractStrandedItemGroups`
   removes groups under the collection group that are library-linked to one of its downloads and
   carry no tag at all. Legacy half-tagged items are outside that set by construction.

   **Deliberately not changed: `GetStatusObservable`.** The UI projection still keys on the parent
   link alone, so it can briefly disagree with `GetStatus`. Adding the tag check there means the
   observable must also *observe* the tag datom — it is written in a later transaction than the group
   — or the UI would stall at "in library" through every normal standard-chain install. The harm
   S5-1 describes is the install job skipping a stranded item, which is the static path; the
   observable is worth revisiting only alongside a tag-datom subscription.

   Related smaller deferrals, still open and not part of S5-1: enabled-group aborts leave uncurated
   partial state (A-3); download rules committed in a detached second tx with no repair path (A-4);
   hash-mismatched curator patches deploy the unpatched original with only a warning (A-5).

7. **Sort-order creation race (B-3)** — `GetOrCreateSortOrderFor` is check-then-create;
   the seeder can race `SortOrderManager`'s CollectionGroup subscription into duplicate
   SortOrder entities, stranding the curated order in the shadowed one. Detect-after-commit
   or a tx-function uniqueness check. Watch live logs for "Multiple SortOrder entities"
   after collection installs to confirm incidence. **B-8 and B-9 are now closed**: the CAS
   retry got exponential backoff, 6 attempts and a typed `SortOrderConflictException` (#101,
   confirmed by CI going green and staying green), and reconciliation is now batched over a
   500 ms window instead of running per commit. What remains open here is only the
   *structured* half of "surface seed failure": `InstallCollectionJob` returns a DB entity,
   not a result record, so a seed failure is still reported as an aggregate log line (which
   now names the consequence) rather than something the UI can show. Threading a warnings
   channel out of that job is its own PR, and it is the same theme as A-5 (see item 6).

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
19b. **Load-sensitive flake: `DownloadsServiceTests.Validate_Download_Jobs_Lifetime`** —
    observed failing once during a full-solution run (2026-07-30) on
    `SyncHelpers.WaitForCollectionCount(..., 30s)`, and passing 3/3 in 144 ms in isolation. It
    is offline, so it runs in the Clean Environment lane, where contention is exactly what
    provokes it — expect intermittent red. Pre-existing (untouched since the R5 rebrand) and
    unrelated to the change that surfaced it. Fix direction: wait on an observable signal rather
    than polling a collection for a wall-clock duration.
19c. **Suspected `HttpDownloadJob` retry-path corruption — decision needed** — the hermetic
    `DownloadsFromAServerThatDoesNotSupportRanges` test (added in #103) failed **once** in a
    full-solution run with correct file size but wrong content, and did not reproduce in ~56
    attempts including 50 downloads under deliberate CPU load, nor in any CI run since. Ruled out:
    a missing flush (`StreamProgressWrapper` disposes its inner stream), and a server-side error
    (nothing logged). That leaves the resilience-retry path as the leading suspect — specifically
    the reset branch in `HttpDownloadJob.StartAsyncImpl` that handles a 200 response arriving for
    a range request, which is the one path #103 deliberately did not cover because reaching it
    needs a timing-dependent pause/resume cycle.

    Why this is not just a flaky test: "correct size, wrong content" is the signature of a
    **truncated download**, because the destination is pre-allocated to the advertised
    Content-Length. If the retry path can leave a partially-written file that reports success, that
    is silent mod corruption. The test is therefore deliberately **not** marked `FlakeyTest`; its
    assertion now reports the first differing offset and any zero-tail length, so the next
    occurrence carries its own evidence. **Decision: quarantine it, or invest in a deterministic
    resume test (inject the retry rather than provoking it with load).**

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
23. ~~**Plugin-header cache (B-7)**~~ — **measured 2026-07-28, decided against.** In-app
    `PluginsFile.MakeMetadata` costs 255 ms cumulative for 682 plugins against a ~2 s gate,
    and standalone Mutagen header parsing is 0.35 s cold / 0.06 s warm, so the parse was
    never the cost. The Nx stream layer does add ~4×, but the absolute number is far too
    small to justify a cache with a lifetime and an invalidation story. Only the cold
    `MissingMasterEmitter` pass (2.1 s, once per app start) approaches the gate, and it is
    dominated by first-touch I/O a cache would also pay. Do not re-open without a workload
    materially larger than a 682-plugin loadout.
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
