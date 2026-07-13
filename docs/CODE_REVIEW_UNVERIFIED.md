# Code Review — Unfinished Verification (handoff)

The review in [`CODE_REVIEW.md`](./CODE_REVIEW.md) ran all 14 domain reviewers to completion,
but the **adversarial verification pass** was cut short by a spend limit. This file is the
handoff: exactly which findings still need verification, plus the method to reproduce, so a
later session can finish the job and produce a fully-verified review.

## What "verification" means here

Each significant finding is handed to a fresh agent whose *only* job is to **refute** it — to
re-read the cited source (plus enough callers/callees/config to judge reachability) and decide
whether the failure scenario is actually reachable in practice, or whether the reviewer missed
a guard, misread a line, or overstated severity. A finding that survives is `verified`; one
that's real but overstated gets a `correctedSeverity`; one that's wrong is `refuted`. This pass
already caught real errors in the completed dimensions (2 refutations, several severity
downgrades — e.g. a "critical" directory-delete claim dropped to low once proven unreachable),
so it's worth running on the remainder rather than trusting the raw reviewer output.

## Status

| | |
|---|---|
| Findings queued for verification | ~139 |
| Verified (got a verdict) | 102 |
| **Still need verification** | **37** (listed below) |
| Completeness critic ("what did everyone miss?") | **never ran** |

The 37 unverified findings are exactly those matching the verification filter — **critical/high
severity, plus medium findings in the bug / performance / security categories** — that fell in
the five dimensions whose verifier agents died on the spend limit (`ui-performance`,
`startup-di`, `tests-benchmarks`, `security`, `fork-delta`) plus two stragglers in
`async-correctness`. (Low/info findings and non-bug mediums were never in scope for
verification by design.) I hand-checked the highest-impact ones before publishing — the dead CI
lanes, the 5 GB backup-cap data-loss path, and the two startup process bugs all hold — but they
have not been through the formal adversarial pass.

---

## The 37 findings still needing verification

### tests-benchmarks (11)

- **[high]** Both fork-added anti-data-loss guards in the synchronizer are completely untested — `src/Apocrypha.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs:1052`
- **[high]** Proton `user.reg` registry rewriting has zero tests despite mutating a prefix-critical file — `src/Apocrypha.Games.BepInEx/RunBepInExGameTool.cs:47`
- **[high]** "Remove duplicates" deletes library items via an untested selection pipeline — `src/Apocrypha.App.UI/Pages/Library/LibraryDuplicateFinder.cs:22`
- **[high]** Modpack install orchestration (ror2mm handler), including the duplicate-download prevention it exists for, has no tests — `src/Apocrypha.App.Cli/Types/IpcHandlers/Ror2mmIpcProtocolHandler.cs:70`
- **[high]** Collection-install end-to-end tests never run anywhere: the `RequiresNetworking` partition is dead in this fork — `tests/Apocrypha.Collections.Tests/CollectionInstallTests.cs:16` *(hand-verified — confirmed real)*
- **[high]** The download pipeline every mod source depends on has one test, and it never runs — `tests/Networking/Apocrypha.Networking.HttpDownloader.Tests/HttpDownloadJobWorkerTests.cs:20` *(hand-verified — confirmed real)*
- **[high]** File-conflict priority machinery (which mod wins on disk) has no directed test — `src/Apocrypha.DataModel/LoadoutManager.TxFuncs.cs:22`
- **[high]** Real `FileHashesService` is stubbed out of every game test — the subsystem that already caused install-deletion has no direct coverage — `src/Apocrypha.Games.FileHashes/FileHashesService.cs:1`
- **[med]** Migration 0009 (conflict priorities) breaks the one-test-per-migration pattern — `src/Apocrypha.DataModel.SchemaVersions/Migrations/_0009_AddLoadoutItemGroupPriority.cs:38`
- **[med]** Benchmarks measure none of the fork's hot paths and carry dead upstream artifacts — `benchmarks/Apocrypha.Benchmarks/Benchmarks/EnumerateFiles.cs:21`
- **[med]** `ThunderstoreLibrary.GetOrAddVersion` has an untested find-or-create race — the DB-level root of the duplicate-entry bug — `src/Apocrypha.Networking.Thunderstore/ThunderstoreLibrary.cs:48`

### startup-di (9)

- **[high]** Unresponsive-process kill hack can SIGKILL a busy-but-healthy main (entire tree) or an innocent recycled-PID process — `src/Apocrypha.App/Program.cs:173` *(hand-verified — `process.Kill(entireProcessTree:true)` after a 6s heartbeat is real)*
- **[high]** Second GUI launch of an already-running app crashes with an unhandled exception instead of focusing the running instance — `src/Apocrypha.App/Program.cs:79` *(hand-verified — pre-`try` startup work throws with no handler)*
- **[med]** nxm/ror2mm handoff drops links when main startup exceeds 60s and on stale-port SocketException — `src/Apocrypha.App/Program.cs:250`
- **[med]** `SettingsManager` is not thread-safe: unsynchronized Dictionary mutated concurrently during startup — `src/Apocrypha.Backend/Settings/SettingsManager.cs:21`
- **[med]** Fresh-install detection is broken: hosted services open the DB (and create its directory) before the `modelExists` check, making `InitialSetup` dead code — `src/Apocrypha.App/Program.cs:87`
- **[med]** `MultiProcessSharedArray` constructor retries IOException in an unbounded 100% CPU busy-loop — `src/Apocrypha.SingleProcess/MultiprocessSharedArray.cs:35`
- **[med]** URI scheme registration rewrites the desktop file and force-steals the default handler on every launch, once per scheme — `src/Apocrypha.App.Cli/UriSchemeRegistration.cs:29`
- **[med]** First game-detection scan (Steam/Heroic/Wine-registry parsing) runs synchronously on the UI thread at page activation — `src/Apocrypha.App.UI/Pages/MyGames/MyGamesViewModel.cs:117`
- **[med]** Unbounded blocking migration before first paint with no splash screen; startup `.Wait(timeout)` calls silently continue on timeout — `src/Apocrypha.App/Program.cs:109`

### ui-performance (8)

- **[high]** Serializing `TextEditorPageContext` (`OneOf<...>`) throws, silently preventing the entire window layout from saving — `src/Apocrypha.App.UI/Pages/TextEdit/TextEditorPage.cs:15`
- **[high]** Search filter synchronously materializes all child rows on the UI thread and returns stale/missing results — `src/Apocrypha.App.UI/Controls/TreeDataGrid/Filters/Filter.cs:39`
- **[high]** All root models are activated eagerly, defeating virtualization: full subscription graph + thumbnail load per row on page open — `src/Apocrypha.App.UI/Controls/TreeDataGrid/TreeDataGridAdapter.cs:153`
- **[med]** `FilterLoadoutItems` observes every Nexus mod page in the whole datastore — missing the per-game pre-filter the library side has — `src/Apocrypha.App.UI/Pages/NexusModsDataProvider.cs:344`
- **[med]** `CustomSortComparer` grids (load order, file conflicts) full-sort + Reset the TreeDataGrid and wipe selection on every changeset — `src/Apocrypha.App.UI/Controls/TreeDataGrid/TreeDataGridAdapter.cs:168`
- **[med]** `OverlayController` performs synchronous `Dispatcher.Invoke` while holding its lock — cross-thread deadlock risk *(note: the same bug WAS verified as `high` under the async-correctness dimension — see `OverlayController.cs:51` in CODE_REVIEW.md; this ui-performance duplicate can be closed against that verdict)*
- **[med]** `LoadoutViewModel` selection-count subscription is never added to disposables — accumulates one live subscription per page activation — `src/Apocrypha.App.UI/Pages/LoadoutPage/LoadoutViewModel.cs:571`
- **[med]** Remove-duplicates scans every `LibraryFile` entity (including nested archive entries) synchronously on the UI thread — `src/Apocrypha.App.UI/Pages/Library/LibraryDuplicateFinder.cs:26`

### security (3)

- **[med]** Nexus OAuth refresh/access tokens and API key stored in plaintext at rest — `src/Apocrypha.Networking.NexusWebApi/Auth/JWTToken.cs:19`
- **[med]** Downloaded mod bytes are never verified against the source's advertised hash (Nexus/Thunderstore/mod.io) — `src/Apocrypha.Networking.HttpDownloader/HttpDownloadJob.cs:194`
- **[med]** 7z/rar extraction can restore symlinks that escape the temp dir; enumeration then follows them — `src/Apocrypha.Backend/FileExtractor/Extractors/SevenZipExtractor.cs:248`

### fork-delta (4)

- **[high]** Sync silently sacrifices unrestorable originals when the backup batch exceeds the 5 GB cap — `src/Apocrypha.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs:1239` *(hand-verified — real, but note the code deliberately downgrades pure-deletions to leave-on-disk; the loss is narrowed to files overwritten by an extract, which the code comments as an accepted tradeoff. A verifier should confirm that boundary.)*
- **[high]** All reference data is frozen at vendoring time and remote updates are disabled — the default experience decays with every game patch — `src/Apocrypha.Games.FileHashes/FileHashesServiceSettings.cs:29` *(hand-verified — `EnableRemoteUpdates = false`, hash DB dated 2025-09-30, no fork feed exists)*
- **[med]** Legacy migration hijacks a coexisting genuine NexusMods.App install without consent — `src/Apocrypha.Sdk/LegacyDataMigration.cs:57`
- **[med]** Proton `user.reg` patch inserts a duplicate winhttp value when the user has an explicit non-matching override, and races wineserver — `src/Apocrypha.Games.BepInEx/RunBepInExGameTool.cs:68`

### async-correctness (2)

- **[med]** `DesktopPortalConnectionManagerWrapper` never releases its semaphore and ignores the `WaitAsync` timeout result — `src/Apocrypha.Backend/OS/LinuxInterop.cs:255`
- **[med]** `ProcessRunner.RunAsync(Process)`: non-Try TCS completion plus Kill-vs-Exited race throws from the cancellation callback — `src/Apocrypha.Backend/Process/Runner.cs:113`

---

## Also not run: the completeness critic

A final pass was designed to ask *"what did everyone miss?"* — enumerate the `src/`
subdirectories no reviewer covered, flag owner-questions left unanswered, surface contradictions
between dimensions, then investigate the top gaps directly. It never ran. A future review
should include it. Known candidate blind spots worth a fresh look (none were a dedicated
dimension): the FOMOD / guided-installer XML + scripting path, the Bannerlord and Larian (BG3)
game modules specifically, the `Apocrypha.Collections` FOMOD-in-collection flow, the Epic /
GOG store locators beyond their download clients, and the diagnostics/health-check emitter
system.

---

## How to reproduce the verification (for whoever picks this up)

Hand each pending finding to a fresh agent with this prompt (the exact template used for the
102 that were verified). It expects a structured verdict: `isReal` (bool), `confidence`
(high/medium/low), `notes` (with file:line evidence actually read), and optional
`correctedDetail` / `correctedSeverity`.

> You are an adversarial verifier in a code review of the repo (C#/.NET Avalonia mod manager
> "Apocrypha"). Another reviewer claims the following finding. Your job is to try to **REFUTE**
> it by reading the actual code — the file it cites, plus enough callers/callees/config to know
> whether the failure scenario is reachable in practice. Do not trust the claim's quotes;
> re-read the source yourself. If the code has guards the reviewer missed, if the "hot path"
> runs once at startup, if the type makes the race impossible, or if the cited line does not say
> what they claim — refute it. If the evidence is ambiguous or you cannot locate the cited code,
> set `isReal=false` with `confidence=low`. If it IS real but overstated/understated, set
> `isReal=true` and provide `correctedDetail`/`correctedSeverity`.
>
> FINDING: `<paste the finding's title, file, severity, category, detail, and suggestion>`

The full finding text (detail + suggested fix) for every item above lives in the machine
output of the original run, and the workflow can be **resumed from cache** so the 102 already-
verified findings don't re-run — only the 37 pending ones plus the completeness critic execute:

```
Workflow({
  scriptPath: "<session>/workflows/scripts/apocrypha-full-review-wf_7b576291-f17.js",
  resumeFromRunId: "wf_7b576291-f17"
})
```

(That run id is session-scoped; in a new session, re-run the review workflow fresh — it will
re-review and re-verify from scratch, which is also a fine way to get an independent second
opinion on the 63 already-confirmed findings.)
