# Code Review — Verification & Completeness (COMPLETE)

*Companion to [`CODE_REVIEW.md`](./CODE_REVIEW.md). The verification pass that was paused by a
spend limit has now been finished, and the completeness-critic pass that never ran has been run.
This file is the authoritative record of both.*

> **Status:** ✅ **Complete.** All 37 previously-unverified findings now have an adversarial
> verdict, and the completeness critic ("what did everyone miss?") has run across seven blind
> spots. The headline result: **36 of the 37 held (1 refuted), and the completeness pass found
> ~33 new code-backed issues the 14-dimension review missed — 7 of them high, including a
> confirmed arbitrary-file-write vulnerability in the FOMOD installer.**

---

## 1. What changed since the handoff

The original review ([`CODE_REVIEW.md`](./CODE_REVIEW.md)) verified 102 of its findings before a
spend limit cut the pass short, leaving **37 findings unverified** and the **completeness critic
un-run**. Both are now done, using the same method: each finding was handed to a fresh agent whose
only job was to **refute** it by re-reading the actual source (plus callers/callees/config/tests)
and judging real-world reachability; each blind spot was handed to a "completeness critic" told to
find only code-backed issues and to say so when an area is clean.

| | Before (handoff) | Now |
|---|---|---|
| Findings verified | 102 | **139** |
| Still unverified | 37 | **0** |
| Refuted (this pass) | — | **1** (`se3`) |
| Completeness critic | never ran | **ran (7 areas)** |
| New findings from completeness | — | **~33 (7 high)** |

---

## 2. The 37 verifications — results

**36 confirmed real, 1 refuted.** Severities below are the verifier's corrected values; "→"
marks a change from the reviewer's original rating. Every verdict was reached by reading the cited
source directly.

### Refuted

| ID | Finding | Location | Why refuted |
|----|---------|----------|-------------|
| `se3` | 7z/rar extraction can restore symlinks that escape the temp dir | `SevenZipExtractor.cs:248` | **Refuted (high confidence), empirically.** The verifier ran the bundled `runtimes/linux-x64/native/7zz` (21.03) with the extractor's exact flags (`x -bsp1 -y`, no `-snl`) and confirmed escaping symlinks are **dropped** ("Sub items Errors"); even forcing `-snl`, 7zz blocks them (`ERROR: Dangerous link path was ignored`). Not exploitable as configured. Residual note: the defense lives in the external binary, so adding `-snl` or a manual extractor would reintroduce the need for app-side filtering. |

### Confirmed (severity corrections applied)

| ID | Sev | Finding | Location | Verifier note |
|----|-----|---------|----------|---------------|
| `tb1` | high | Both fork-added anti-data-loss guards are untested | `ALoadoutSynchronizer.cs:1052` | No directed test for `GuardAgainstVanishedGameFiles` or the >5 GB cap; guard branches unreachable with the stubbed test data. |
| `tb4` | high | Modpack install orchestration (ror2mm) has no tests | `Ror2mmIpcProtocolHandler.cs:70` | `Handle()` (install + `InFlightPackages`/`InFlightInstalls` dedup) has zero coverage; only the URL parser is tested. |
| `tb7` | high | File-conflict priority machinery has no directed test | `LoadoutManager.TxFuncs.cs:22` | `ResolveFileConflictsTxFunc` index-shuffle math (Debug.Assert-guarded) untested; MoveItems test only checks priority *count*. |
| `tb8` | **med→high** | Real `FileHashesService` stubbed out of every game test | `FileHashesService.cs:1` | `AddDefaultServicesForTesting` defaults `stubbedFileHashService=true`; no test passes false. The 1022-line real service — which once caused install deletion — is never driven. |
| `sd1` | high | Unresponsive-process kill hack can SIGKILL a healthy main / recycled PID | `Program.cs:173` | `process.Kill(entireProcessTree:true)` after a 6 s heartbeat; `SyncFile.GetProcessById` does no name/start-time validation, so a recycled PID kills an innocent process. |
| `up1` | high | Serializing `TextEditorPageContext` (`OneOf<…>`) throws, blocking window-layout save | `TextEditorPage.cs:15` | No `OneOf` converter registered anywhere; `OneOf.AsT0/AsT1` throw for the inactive case; `WindowManager.SaveWindowState`'s single try/catch means one throw aborts saving the whole layout. |
| `up3` | high | Eager root-model activation defeats virtualization | `TreeDataGridAdapter.cs:153` | Changeset `.Do` activates every root before adding to `Roots`, wiring the full subscription graph and firing one thumbnail `LoadResourceAsync` per root (~1000 for a 1000-mod library). |
| `sd6` | med | `MultiProcessSharedArray` ctor retries IOException in an unbounded busy-loop | `MultiprocessSharedArray.cs:35` | `while(true){try{…break;}catch(IOException){continue;}}` — no sleep/backoff/bound; a persistent sharing violation spins at 100% CPU. |
| `sd3` | med | nxm/ror2mm handoff drops links on >60 s startup and stale-port SocketException | `Program.cs:250` | Wait loop returns 1 (drops link) after 60 s; `CliClient.ExecuteCommand` re-throws `SocketException`, uncaught by `catch(NoMainProcessStarted)`. |
| `sd4` | med | `SettingsManager` not thread-safe | `SettingsManager.cs:21` | Reviewer's "multiple hosted services" mechanism was wrong, but a real race exists: the update check runs on the thread pool (`.ObserveOnThreadPool()`) and mutates the unsynchronized `_values` dict concurrently with UI-thread settings reads. |
| `sd5` | med | Fresh-install detection is dead code | `Program.cs:87` | `DatomStoreSettings` factory creates the DB dir on resolution; `ThunderstoreCommunityBackfill` (hosted, needs `IConnection`) forces that during `StartAsync`, so `modelExists` is always true by line 87 and `InitialSetup()` is unreachable. |
| `sd7` | med | URI scheme registration rewrites the desktop file + steals default handler every launch | `UriSchemeRegistration.cs:29` | Hosted `BackgroundService`, no "already registered" short-circuit; for AppImage/Manual installs it rewrites the desktop file and runs `xdg-settings set default-url-scheme-handler` per scheme on every launch. |
| `sd8` | med | First game-detection scan runs synchronously on the UI thread | `MyGamesViewModel.cs:117` | `WhenActivated` calls `gameRegistry.LocateGameInstallations()` (full Steam/Heroic/Wine-registry scan) with no `Task.Run`; MyGames is the default landing page and nothing warms the cache. |
| `sd9` | med | Unbounded blocking migration before first paint; `.Wait(timeout)` silently continues | `Program.cs:109` | `MigrateAll().Wait()` has no timeout and runs before the Avalonia UI with no splash; the `.Wait(timeout)` calls at 76/79/121 discard the bool, silently proceeding on timeout. |
| `up2` | **high→med** | Search filter synchronously materializes all child rows on the UI thread | `Filter.cs:39` | `MatchesChildren`→`InitAndGetChildren` forces full child materialization (documented in the file's own comment); stale-result manifestation is timing-dependent, so downgraded. |
| `up4` | med | `FilterLoadoutItems` observes every Nexus mod page in the datastore | `NexusModsDataProvider.cs:344` | Loadout side lacks the cheap per-game `.FilterImmutable(… GameId ==)` pre-filter the library side has; results stay correct but subscriptions are set up for every mod page across all games. |
| `up5` | med | `CustomSortComparer` grids full-sort + Reset + wipe selection on every changeset | `TreeDataGridAdapter.cs:168` | `Roots.Sort(comparer)` inside the per-changeset `.Do` raises a `Reset`, which the adapter turns into a selection wipe. Confirmed against ObservableCollections 3.3.4. |
| `up6` | **med→high, duplicate** | `OverlayController` sync `Dispatcher.Invoke` while holding its lock | `OverlayController.cs:51` | Same code path already confirmed **high** under async-correctness (`CODE_REVIEW.md` appendix). Close this against that verdict. |
| `up7` | med | `LoadoutViewModel` selection-count subscription never disposed | `LoadoutViewModel.cs:571` | Line 571 `.Subscribe(...)` has no `.AddTo(disposables)` while every sibling in the same `WhenActivated` block does; leaks one subscription per activation. |
| `up8` | **med→low** | Remove-duplicates scans every `LibraryFile` synchronously on the UI thread | `LibraryDuplicateFinder.cs:26` | Confirmed on-UI-thread and enumerates nested archive entries, but only on an explicit click with `AwaitOperation.Drop` re-entrancy guard; bounded impact. |
| `se1` | med | Nexus OAuth tokens + API key stored in plaintext at rest | `JWTToken.cs:19` | `AccessToken`/`RefreshToken`/`ApiKey.Key` are plain `StringAttribute` written to MnemonicDB; repo-wide grep finds no keyring/SecretService/DPAPI. Needs local FS access to exploit, but the refresh token grants durable account access. |
| `se2` | med | Downloaded bytes never verified against the source's advertised hash | `HttpDownloadJob.cs:194` | Corrected: **not "never" globally.** Collection flows *do* verify (`ExternalDownloadJob` MD5, `InstallCollectionDownloadJob` CRC32). But a normally-downloaded Nexus/mod.io/Thunderstore mod is installed with no integrity check; mod.io's advertised md5 isn't even deserialized. HTTPS mitigates casual MITM, leaving CDN-compromise/corruption. |
| `tb2` | **high→med** | Proton `user.reg` rewriting has zero tests | `RunBepInExGameTool.cs:47` | Confirmed no test touches `EnsureWinhttpOverride`/`user.reg`; downgraded because it's a coverage gap, not itself a runtime defect (it hides `fd4`). |
| `fd4` | med | Proton `user.reg` patch inserts a duplicate `winhttp` value; races wineserver | `RunBepInExGameTool.cs:68` | The only guard is `Contains("\"winhttp\"=\"native,builtin\"")`; an existing `"winhttp"="disabled"` fails it and a second key is inserted *above* the user's, so the user's value wins on load. No wineserver coordination before the write. |
| `tb3` | **high→med** | "Remove duplicates" deletes items via an untested selection pipeline | `LibraryDuplicateFinder.cs:22` | Confirmed no test for `FindRemovableDuplicates`; non-trivial keep-oldest/keep-linked branching is destructive but coverage-gap, not a demonstrated defect. |
| `tb5` | **high→med** | Collection-install e2e tests never run (`RequiresNetworking` dead lane) | `CollectionInstallTests.cs:16` | The only push/PR lane filters `RequiresNetworking!=True`; the only lane that runs them is `workflow_dispatch`-only, self-hosted, needs `NEXUS_API_KEY` the fork lacks. Real but documented/intentional. |
| `tb6` | **high→med** | The one HttpDownloader test never runs | `HttpDownloadJobWorkerTests.cs:20` | Single `[Fact]`, `RequiresNetworking=True`; same dead-lane wiring as `tb5`. Never executes in CI. |
| `tb9` | med | Migration `_0009` breaks the one-test-per-migration pattern | `_0009_AddLoadoutItemGroupPriority.cs:38` | `MigrationSpecificTests/` has `_0001,_0003–_0008` but no `_0009` (nor a Migration-9 legacy snapshot). Minor imprecision: `_0002` also lacks a test, so it's "most," not strictly one-per-migration. |
| `tb10` | **med→low** | Benchmarks measure none of the fork's hot paths; carry dead upstream artifacts | `EnumerateFiles.cs:21` | Confirmed dead artifact (hardcoded Windows path, old-vs-new comparison); but a relevance opinion, not a defect/coverage gap. |
| `tb11` | med | `ThunderstoreLibrary.GetOrAddVersion` has an untested find-or-create race | `ThunderstoreLibrary.cs:48` | Unguarded find-then-create with an `await GetPackage(...)` widening the window between the read snapshot and the commit; no uniqueness enforcement; untested. |
| `sd2` | **high→med** | Second GUI launch crashes instead of focusing the running instance | `Program.cs:79` | Startup work (76–134) is outside the try/catch (136); a second launch throws unhandled opening the exclusively-held RocksDB, or `SingleProcessLockException` that's never caught. Downgraded: primary instance unharmed, no data loss, but crashing on a common action is a real defect. |
| `ac1` | med | `DesktopPortalConnectionManagerWrapper` never releases its semaphore; ignores timeout | `LinuxInterop.cs:255` | No `.Release()` anywhere in the file; the `WaitAsync(10s)` bool is discarded so a timeout is treated as acquired; the double-checked lock is broken (no re-check of `_instance`). Linux-only. |
| `ac2` | med | `ProcessRunner.RunAsync(Process)`: non-Try TCS + Kill-vs-Exited race | `Runner.cs:113` | `SetResult`/`SetException` (not `Try*`) plus a non-volatile `hasExited` read racing the Exited handler; the cancel callback can `Kill()` a disposing process, then `SetException` on an already-completed TCS, throwing out of `CancellationTokenSource.Cancel()`. |
| `fd1` | **high→med** | Sync sacrifices unrestorable originals over the 5 GB backup cap | `ALoadoutSynchronizer.cs:1239` | Real but narrower than stated: pure deletions among the skipped set are downgraded to leave-on-disk (preserved); loss is confined to files simultaneously **overwritten by an extract**, **skipped from the batch**, and **not otherwise deduped** — and it *is* logged (`LogWarning`), not silent. Common trigger (unknown/frozen version) is usually store-restorable. |
| `fd2` | **high→med** | All reference data frozen; remote updates disabled | `FileHashesServiceSettings.cs:29` | Confirmed: `EnableRemoteUpdates=false` default, no code sets it true, DB snapshot dated 2025-09-30, URLs still point at `Nexus-Mods/game-hashes` with a `TODO(linux-fork)`. Downgraded because runtime degrades gracefully to the embedded snapshot; facts fully accurate. |
| `fd3` | med | Legacy migration hijacks a coexisting genuine NexusMods.App install | `LegacyDataMigration.cs:57` | `LegacyDataDirectoryName = "NexusMods.App"` combined with Linux XDG base dirs resolves to the real `~/.local/share/NexusMods.App`, unconditionally `Directory.Move`d to `Apocrypha` with only an exists/not-exists guard — no consent prompt, no fork marker. Recoverable rename, requires coexistence. |

---

## 3. Completeness critic — new findings the 14-dimension review missed

Seven blind spots were investigated. Each critic was told to report only code-backed issues and to
declare an area clean rather than invent problems. **~33 new findings surfaced, 7 of them high.**
The standout is a confirmed arbitrary-file-write in the FOMOD installer.

### 3.1 🔴 Headline: FOMOD arbitrary file write (path traversal) — HIGH (arguably critical)

| Sev | Finding | Location |
|-----|---------|----------|
| **high** | **FOMOD destination paths get no `..` sanitization → arbitrary file write of attacker-controlled bytes outside the game directory.** | `FomodXmlInstaller.cs:173/236/254` (+ `RemoveRoot:178`) |

**Confirmed exploitable end-to-end (high confidence).** A dedicated verifier traced every hop:
`RelativePath.FromUnsanitizedInput` (NexusMods.Paths, `PathHelpers.Sanitize`) only converts
`\`→`/`, collapses separators, and trims trailing ones — it does **not** strip, collapse, or reject
`..`. `RemoveRoot` only trims leading slashes. `Join` and `AbsolutePath.Combine`/`JoinParts` are
pure concatenation. At deploy, `ALoadoutSynchronizer.ActionExtractToDisk` → `GameLocations.ToAbsolutePath`
→ `NxFileStore.ExtractFiles` writes through `System.IO`, where the **OS kernel resolves the literal
`..`** and writes outside the game directory. The only `InFolder` guard in the installer filters
*source* archive members, not *destinations*. So a malicious FOMOD (a third-party mod download) whose
`ModuleConfig.xml` sets `destination="..\..\..\<path>\evil.dll"` writes attacker content to an
arbitrary location on apply → potential code execution. **Fix:** reject or normalize-and-contain `..`
in FOMOD destinations (and, defense-in-depth, add a containment assertion in the synchronizer before
any write). *For contrast, `ManagedZipExtractor` already defends against this by running names through
`PathsHelper.FixPath` first — the FOMOD path just skips that step.*

### 3.2 FOMOD / guided installer (`Apocrypha.Games.FOMOD`)

| Sev | Cat | Finding | Location |
|-----|-----|---------|----------|
| med | bug | Cancellation token dropped — the guided installer can't be cancelled while a step is displayed (`Execute(...)` and `RequestUserChoice(..., CancellationToken.None)`) | `UiDelegate.cs:148`, `FomodXmlInstaller.cs:125` |
| med | bug | `PresetGuidedInstaller` throws `IndexOutOfRangeException` when the FOMOD presents more steps than the preset recorded (also on empty `[]`) — unbounded `_steps[_currentStep]` | `PresetGuidedInstaller.cs:35` |
| med | bug | Stubbed condition/context delegates silently produce wrong install plans; `GetCurrentGameVersion()=""` → `new Version("")` throws on version-gated FOMODs | `ContextDelegates.cs:69/60/32`, `PluginDelegates.cs:31` |
| med | bug | A source file the path-fixer can't remap is silently skipped, but the install still returns `Success` — silent partial install | `FomodXmlInstaller.cs:238/137` |
| med | bug | Fire-and-forget `ContinueWith` reads `task.Result`; a throw before `_continueToNextStep` hangs the executor and the exception is unobserved | `UiDelegate.cs:149` |
| low | securi | Untrusted `ModuleConfig.xml` handed to the external `FomodInstaller.Scripting.XmlScript` parser with no in-repo XXE/DTD control (mitigation delegated to an un-vendored package) | `FomodXmlInstaller.cs:122/140` |
| low | mainta | Install errors surfaced as a bare `System.Exception` — callers can't distinguish authoring errors from internal faults | `FomodXmlInstaller.cs:131` |

### 3.3 FOMOD-in-collection flow (`Apocrypha.Collections`)

| Sev | Cat | Finding | Location |
|-----|-----|---------|----------|
| **high** | bug | An interactive guided-installer window **pops up during an unattended collection install** when a FOMOD mod's `choices` is null (falls through to the interactive `UiDelegates`) | `InstallCollectionDownloadJob.cs:141` |
| **high** | bug | The process-singleton `FomodXmlInstaller`'s `_delegates` is mutated without synchronization during `Parallel.ForEachAsync` collection installs — mod A can apply mod B's preset/archive | `FomodXmlInstaller.cs:112` |
| med | bug | Preset choices silently not honored on group/option **name mismatch** (name-only LINQ join, no validation) → required-file group left unselected, silently broken mod | `PresetGuidedInstaller.cs:38` |
| med | bug | `FomodChoice.idx` is parsed but never used — duplicate option names resolve to the wrong or multiple selections | `FomodOptions.cs:28` |
| med | bug | The shared `UiDelegates` replaced by the preset installer is never restored → a later *interactive* FOMOD install reuses stale preset state (crash or silent wrong choices) | `FomodXmlInstaller.cs:107` |

### 3.4 Epic / GOG locators (Linux game discovery)

| Sev | Cat | Finding | Location |
|-----|-----|---------|----------|
| **high** | bug | EGS/GOG locators never set `Platform`, so a Windows-via-Wine install is mislabeled native Linux → wrong exe/version resolved (repro: Heroic/Wine Stardew Valley returns the Linux binary name) | `GOGLocator.cs:67`, `EGSLocator.cs:47`, `HeroicGOGLocator.cs:56` |
| **high** | extens | No Heroic/Legendary EGS locator on Linux — Epic games installed via Heroic (the normal path) are undetectable, while the equivalent GOG game works | `ServiceExtensions.cs:41` |
| med | bug | Wine-prefix EGS/GOG discovery only scans `~/.wine` and `$WINEPREFIX`, missing Bottles/Lutris/Heroic per-game prefixes | `WinePrefixWrappingLocator.cs:29` |

*Checked and clean: `EGSGame.ManifestHash.Select`, `GOGGame.BuildId.ToString()`, and per-locator
error handling (OneOf results + `GameRegistry` try/catch) are all safe.*

### 3.5 Diagnostics / health-check emitters

| Sev | Cat | Finding | Location |
|-----|-----|---------|----------|
| **high** | perfor | `DiagnosticManager` never evicts per-loadout observables — each loadout ever viewed keeps a hot `Replay(1)` pipeline re-running the full diagnostic pass (incl. `ISMAPIWebApi` calls) on every DB revision for the whole session | `DiagnosticManager.cs:46` |
| med | perfor | The full sync tree is built on every diagnostic pass even for games (Bannerlord, BG3) whose emitters never consume it | `DiagnosticManager.cs:88` |
| med | bug | Duplicate `DiagnosticId(Bannerlord, 16)` shared by `MissingProtontricksForRedMod` and `MissingBLSETemplate`, which can co-occur → ID-keyed dedup/dismissal conflates them | `Diagnostics.cs:820/837` |
| low | bug | `MissingMasterEmitter`'s legacy `Diagnose` overload is `throw new NotImplementedException()` — a landmine for any caller of the pre-sync-tree path (tests already call it elsewhere) | `MissingMasterEmitter.cs:25` |
| low | mainta | A `static` lock guards instance-level diagnostic caches | `DiagnosticManager.cs:24` |

*Checked and clean: the emitter core is robust — each emitter is wrapped in its own try/catch inside
`Parallel.ForEachAsync`, severity mapping is consistent, and no emitters are dead.*

### 3.6 Bannerlord module

| Sev | Cat | Finding | Location |
|-----|-----|---------|----------|
| med | bug | The launch load order includes **disabled** modules (no `IsEnabled()` filter, unlike the diagnostic emitter and `LocateBLSE`) — "disable" doesn't remove a mod from the forced `_MODULES_` list | `Helpers.cs:19` → `BannerlordRunGameTool.cs:81` |
| med | bug | Copy-paste typo: `ModuleMultiplayer` is defined as `"Modules/BirthAndDeath"` (dup of `ModuleBirthAndAgingOptions`), so vanilla `Modules/Multiplayer` is never in the backup-ignore set and gets copied into the file store on ingest | `BannerlordLoadoutSynchronizer.cs:31` |
| low | securi | `SubModule.xml` parsed via `XmlDocument.Load` with `DtdProcessing` not set to `Prohibit` → billion-laughs entity-expansion DoS on install/diagnostics (XXE proper is mitigated by net9's null default resolver) | `BannerlordModInstaller.cs:192`, `Pipelines.cs:38` |

### 3.7 Baldur's Gate 3 / Larian `.pak` parser

| Sev | Cat | Finding | Location |
|-----|-----|---------|----------|
| med | bug | A single `Stream.Read()` is assumed to return the full decompressed size — valid zlib/zstd `.pak` entries larger than one inflate chunk are falsely rejected as corrupt (should be `ReadExactly`) | `PakFileParser.cs:245` |
| med | securi | Untrusted `.pak` header fields (`numOfFiles`, `compressedSize`, `UncompressedSize`) drive unvalidated allocations / Int32 overflow → OOM DoS; these exception types also bypass the pipeline's `InvalidDataException`-only catch | `PakFileParser.cs:128-134/225` |
| low | bug | `FileListOffset` (UInt64) truncated to `int` → parse corruption for `.pak` files larger than 2 GB | `PakFileParser.cs:27` |
| low | bug | `meta.lsx` located by unanchored `.Contains("meta.lsx")` — can select the wrong file (`meta.lsx.bak`, a decoy module) → wrong dependency diagnostics | `PakFileParser.cs:30` |
| info | mainta | Swapped operands in the file-list decompression-mismatch error message | `PakFileParser.cs:148` |

*Checked and clean: the LSX/modsettings XML parsers use `XmlReader.Create` with net9 defaults
(`DtdProcessing.Prohibit`, null resolver) — no XXE/billion-laughs there.*

### 3.8 Garbage collection & single-process (previously-uncovered projects)

| Sev | Cat | Finding | Location |
|-----|-----|---------|----------|
| med | bug | GC runs on a thread-pool task against a **stale DB snapshot**, guarded only by the file-store lock; a content-hash-dedup transaction committing after the snapshot but before the delete leaves a live item pointing at a dropped archive (TOCTOU) | `RunGarbageCollector.cs:25`, `DataStoreReferenceMarker.cs` |
| med | archit | GC correctness depends on an acknowledged **closed set of 3 referencing entity types**; any other hash-holder (or a live loadout reporting `IsValid()==false`) → permanent deletion, with no fail-safe guard | `DataStoreReferenceMarker.cs:22` |
| med | bug | Single-instance election treats **any** live process owning the recorded PID as "main is running" (no identity check) — a recycled PID after a crash wedges startup/CLI. Shares `sd1`'s `SyncFile` root cause | `SyncFile.cs:52` |
| low | securi | `MultiProcessSharedArray` bounds checks are compiled out in Release (`#if DEBUG`) — latent OOB native write; not currently reachable (sole caller uses index 0) | `MultiprocessSharedArray.cs:107` |

*Checked and clean: `ManagedZipExtractor` defends against zip-slip via `PathsHelper.FixPath` (strips
`..`). Residual low/low: `outputStream.SetLength(zipEntry.Size)` trusts the attacker-controlled
uncompressed size — potential preallocation DoS on non-sparse filesystems.*

---

## 4. Themes that emerged

1. **Untrusted mod-file parsing is systematically under-hardened.** The FOMOD arbitrary-write,
   the Bannerlord `SubModule.xml` DoS, and the BG3 `.pak` allocation DoS are the same class: files
   from third-party downloads are parsed/deployed without traversal or resource-bound guards. The
   codebase *has* the right pattern (`ManagedZipExtractor` + `PathsHelper.FixPath`); it just isn't
   applied on these paths. Treat "harden every untrusted-mod-file entry point" as one workstream.
2. **The FOMOD preset/collection path is the fork's most bug-dense new area.** Six of the new
   findings are in the non-interactive FOMOD-in-collection flow (a singleton race, an interactive
   popup mid-unattended-install, an unbounded index, name-only matching with no validation). It
   deserves a focused hardening + test sprint before collections lean on it further.
3. **Linux game discovery beyond Steam is incomplete.** The Platform-not-set bug and the missing
   Heroic-EGS locator both mean the Linux-first fork silently mishandles or misses Wine/Heroic Epic
   & GOG installs — high-value for the target audience.
4. **Lifecycle leaks recur in the reactive UI/diagnostics layer** (`up7`, the diagnostics
   no-eviction leak) — subscriptions created per-activation/per-loadout without a matching teardown.

---

## 5. Method (unchanged from the original pass)

Each verifier was given only the finding (title, file, severity, category) and told to **refute** it
by reading the actual source — the exact template from the original run. Each completeness critic was
given one blind spot and told to report only file:line-backed issues and to declare clean areas
clean. Verifiers ran as isolated agents with no shared state; the FOMOD-traversal claim got a
dedicated deeper trace because its severity hinged on downstream behavior the first critic didn't
follow. Reviewed against `linux-fork` at the same tree as `CODE_REVIEW.md` (source identical to
`80037f4`; only these docs differ).
