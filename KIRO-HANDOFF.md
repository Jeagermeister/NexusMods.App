# KIRO HANDOFF — NexusMods.App Linux-First Hard Fork

> Written 2026-07-06 for Kiro IDE to read and analyze. Self-contained: covers the project,
> everything done in the bootstrap sessions, the exact working-tree state, and **two
> directives for future work** (§6 and §7). Companion docs in this repo:
> - `LINUX-FORK-CONTEXT.md` — the original fork rationale, architecture map, milestones.
> - `BASELINE-CACHYOS.md` — first-build/first-run findings on CachyOS, with root causes.

---

## 1. Project in one paragraph

Brian (GitHub **Jeagermeister**, CachyOS/Arch, stack: C#/.NET, Rust, Go, C++, Python, TS, SQL)
is hard-forking the **archived** `Nexus-Mods/NexusMods.App` (C#/.NET 9, Avalonia,
GPL-3.0) into an **independent, Linux-first mod manager**. Nexus abandoned it Jan 2026 for
business reasons; the architecture (MnemonicDB event-sourced datastore, git-like versioned
Loadouts, 3-way-merge deployment engine) is the best in the modding space and sits
uncontested. Upstream is frozen at commit `48284f38c` (~Nov 2025). Every fix and feature
from here on is ours.

## 2. Environment & commands

- Repo: `~/Source/candidates/modding/NexusMods.App`, branch `main`, no fork remote yet.
- .NET SDK 9.0.118 (installed; target is net9.0). SDK 10 also present.
- **First-time setup:** `git submodule update --init --recursive` (SMAPI + docs — build fails without).
- Build: `dotnet build NexusMods.App.sln` (96 projects, 0 errors, ~20s warm).
- Run UI: `dotnet run --project src/NexusMods.App`. CLI: `src/NexusMods.App.Cli`.
- App data: `~/.local/share/NexusMods.App/`; logs: `~/.local/state/NexusMods.App/Logs/`.

## 3. Working-tree state (IMPORTANT — uncommitted work)

No commits have been made. The working tree contains:

| File | Change |
| --- | --- |
| `src/NexusMods.Networking.NexusWebApi/V1Interop/LocalMappingCache.cs` | **Patch 1:** removed a `Debug.Assert` that crashed every Debug-build launch (see §4.2); replaced with a warning log. |
| `src/NexusMods.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs` | **Patch 2:** `MaximumBackupSize` 5GB → 100GB with corrected doc comment (see §4.3). **Superseded by directive §6** — re-do per that spec. |
| `LINUX-FORK-CONTEXT.md`, `BASELINE-CACHYOS.md`, `KIRO-HANDOFF.md` | Untracked docs (this handoff set). |

First git task: create a fork branch, commit patches + docs, add own remote. Fork
name/brand still undecided.

## 4. What happened in the bootstrap sessions (2026-07-06)

### 4.1 Baseline achieved
Full solution builds clean; app launches on Wayland; Nexus **login works**; UI navigation
works; nxm:// handler + .desktop registration succeeded out of the box; Steam locator found
both Steam libraries and detected **Mount & Blade II: Bannerlord** and **Stardew Valley**
installed (second drive, `/mnt/17b46faa-.../SteamLibrary`).

### 4.2 Bug 1 — startup crash from Nexus's live data (FIXED, uncommitted)
`games.json` (game id ↔ domain map) is downloaded **from Nexus's live CDN at build time**
(`NexusMods.Networking.NexusWebApi.csproj`). Live data now contains domain collisions the
frozen code never saw (id 8703 "Neverwinter" vs id 180 "Neverwinter Nights" → `neverwinter`;
id 9193 "WRC 5" vs id 2892 → `wrc5`). A `Debug.Assert` in
`LocalMappingCache.TryParseJsonFile` assumed 1:1 and killed the process.
**Follow-up:** vendor a snapshot of games.json instead of build-time download.

### 4.3 Bug 2 — "Cannot backup 9560 files / 30GB > 5GB" managing Bannerlord (root-caused)
The file-hashes DB (identifies vanilla game files) comes from GitHub releases of
`Nexus-Mods/game-hashes` — **last release 2025-09-30, frozen forever**. Bannerlord has been
patched since → its Steam manifest IDs are unknown → `Unable to find game version` → the
synchronizer can't classify ANY file as a known game file → all 30.2GB flagged
`BackupFile` → tripped the 5GB fuse (`ALoadoutSynchronizer.cs`, `MaximumBackupSize`).
Interim patch raised the fuse to 100GB; **§6 replaces that with the intended behavior**.

**STRATEGIC CONSEQUENCE (biggest finding): the fork must own a game-hashes pipeline.**
Until we generate hash-DB releases for current game versions (Steam depot manifests via
`NexusMods.Networking.Steam` are the natural source), every game patched after Sept 2025
looks 100%-modified. Theme: **cut all umbilicals to Nexus's dead infrastructure**
(games.json at build time, game-hashes releases, `data.nexusmods.com` at runtime).

### 4.4 Vortex migration executed
Brian's Linux Vortex fork (source at `~/Source/Vortex`, runs from source; data at
`~/.config/@vortex/main/`) had deployed 11 mods into Bannerlord via **symlinks**
(manifest: `vortex.deployment.json`, 537 files). We purged them cleanly (symlinks removed,
Vortex `.__folder_managed_by_vortex` markers deleted, 3 runtime-generated BetterBanditsPlus
XMLs stashed to `~/.config/@vortex/main/mountandblade2bannerlord/purge-leftovers-2026-07-06/`).
Game dir is now vanilla + `FastMode` (a manual, non-Vortex install Brian chose to keep).
Mod archives live at `~/.config/@vortex/main/downloads/mountandblade2bannerlord/` (10
archives; Harmony's is missing → re-download via nxm). A `baldursgate3` staging dir also
exists → BG3 (Larian family, supported by the app) is migration candidate #2.

**Feature idea logged: "Import from Vortex" wizard** (parse Vortex staging + deployment
manifests + downloads → auto-populate Library and a loadout). Killer adoption feature;
Brian owns a Vortex fork and knows its formats.

### 4.5 Misc findings
- `protontricks` is only needed when the app itself executes a Windows .exe inside a
  game's Proton prefix: Bannerlord launch-via-BLSE (`BannerlordRunGameTool` →
  `GameToolRunner`) and Cyberpunk RedMod deploy. Deploying mods needs no Wine. Candidate:
  replace protontricks with `umu-launcher`/direct Proton invocation and drop the dependency.
- Bannerlord's `MissingProtontricksEmitter` calls `CreateMissingProtontricksForRedMod`
  (upstream copy-paste bug — message names the wrong game).
- Non-fatal warts: `df --output=source` fails on not-yet-created Archives dir
  (`LinuxInterop.GetFileSystemMount`); sporadic R3 `ObjectDisposedException` on UI teardown.

---

## 5. Architecture quick map (see LINUX-FORK-CONTEXT.md for the full table)

| Concern | Path |
| --- | --- |
| Event-sourced datastore (CROWN JEWEL — do not rewrite) | `NexusMods.MnemonicDB.*` |
| Loadouts / deployment engine (3-way sync) | `src/NexusMods.Abstractions.Loadouts[.Synchronizers]/` |
| Library (downloaded/local archives) | `src/NexusMods.Library/`, `src/NexusMods.Abstractions.Library/` |
| Nexus API + games.json mapping | `src/NexusMods.Networking.NexusWebApi/` |
| File-hashes DB client (frozen upstream feed) | `src/NexusMods.Games.FileHashes/` |
| Steam depots/manifests (future hash source) | `src/NexusMods.Networking.Steam/` |
| Linux interop (nxm, mounts, xdg) | `src/NexusMods.Backend/OS/LinuxInterop*.cs` |
| Proton/Wine tooling | `src/NexusMods.Games.Generic/` (GameToolRunner, protontricks deps) |
| Installer engines | `src/NexusMods.Games.FOMOD/`, `src/NexusMods.Games.AdvancedInstaller/` |
| Game modules (pattern to copy for new games) | `src/NexusMods.Games.StardewValley/`, `.../MountAndBlade2Bannerlord/` |

---

## 6. DIRECTIVE 1 — Backup policy: silent skip instead of error

**Requirement (Brian, 2026-07-06):** revert `MaximumBackupSize` from the interim 100GB back
to **5GB**, and when the computed backup total exceeds it, **do not throw and do not nag** —
quietly skip the backup and continue the operation (loadout creation / sync proceeds).

**Where:** `src/NexusMods.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs`
- `MaximumBackupSize` property (currently patched to `Size.GB * 100` — revert to `* 5`).
- `ActionBackupNewFiles(...)`: the `if (totalSize > MaximumBackupSize)` block currently
  logs an error and `throw`s. Replace with: skip `_fileStore.BackupFiles(...)` (and the
  `GameBackedUpFile` pinning transaction) for the oversized batch and return normally.
  A single `LogInformation`/`LogDebug` line for forensics is acceptable; no user-facing
  diagnostic, no exception.

**Engineering notes for the implementer (analyze before coding):**
- Consequence to accept knowingly: files that never got backed up cannot be restored by the
  app ("remove all mods" / undo / restore-to-vanilla for overwritten game files). For Steam
  games this is recoverable out-of-band (Steam "Verify integrity"); for GOG/Epic less so.
  This tradeoff is accepted because the oversized case today = "unknown game version due to
  frozen hash DB", where the backup would be a redundant 30GB copy of Steam-restorable data.
- Consider skip granularity: the spec is batch-level skip (matches current structure).
  A refinement — back up files *up to* the cap (smallest-first) so small genuinely-modified
  files still get protected — is allowed if it stays silent.
- Check call sites of `ActionBackupNewFiles` for assumptions that backup succeeded
  (`RunActions` / ingest paths); ensure skipping doesn't corrupt sync state. The
  `GameBackedUpFile` pins exist to protect archives from GC — skipping both together is
  consistent.
- The long-term fix that makes this near-moot is the **own game-hashes pipeline** (§4.3);
  once versions are recognized, backup batches return to being small.

## 7. DIRECTIVE 2 — Unify into ONE complete mod platform

**Vision (Brian, 2026-07-06):** this fork should become a **complete, single mod
application** — not just Nexus-sourced game mods. Concretely, absorb the roles of:

1. **Nexus Mods manager** — already the core (Library, nxm://, FOMOD, loadouts).
2. **A Minecraft mod launcher** — the role filled today by CurseForge app / Modrinth app /
   Prism Launcher: manage Minecraft instances, mod loaders (Fabric/Forge/NeoForge/Quilt),
   download mods/modpacks from Modrinth + CurseForge, and launch the game.
3. **r2modman** (Thunderstore ecosystem) — mod manager for Risk of Rain 2, Lethal Company,
   Valheim, and ~every BepInEx-based Thunderstore game; profile-based, one-click installs
   from thunderstore.io.

**Why the architecture can carry this:** the app's core abstractions are already
source-agnostic in the right places — `ILibraryService` (archives in, whatever the origin),
Loadouts (versioned per-game state), `ILibraryItemInstaller` per game, `IGame` modules,
`IJob` downloads. "A mod source" is the missing abstraction: today Nexus is privileged
(`NexusModsLibrary`, nxm handling, collections). The unification work is roughly:

- **Mod-source abstraction layer:** generalize download/metadata/updates so
  Thunderstore API (`thunderstore.io/api/`), Modrinth API (documented, API-key-free),
  and CurseForge API (requires key, ToS constraints on redistribution) sit beside the
  Nexus API as peers. Each source brings: search/browse, version metadata, dependency
  resolution (Thunderstore and Modrinth both have real dependency graphs — richer than
  Nexus), download, update checking, and protocol handlers (`ror2mm://` for Thunderstore
  one-click, Modrinth's `modrinth://`).
- **BepInEx as a first-class framework:** r2modman games are overwhelmingly
  BepInEx+Unity. A generic "BepInEx game" module (install loader, deploy plugins to
  `BepInEx/plugins`, per-loadout profiles) covers dozens of games in one stroke — and
  directly reuses the planned **Subnautica/BepInEx** work from the Vortex fork
  (see LINUX-FORK-CONTEXT.md §additive). This is the natural bridge between the Nexus
  world and the Thunderstore world.
- **Minecraft is the biggest scope jump:** it's not "mods for an installed game" but
  *instance management + game launching* (MS account auth, version manifests, asset
  downloads, JVM management, loader installation). Two viable strategies to analyze:
  (a) implement instances natively (Prism Launcher, ATLauncher — both GPL, both studied
  easily — as references; meta data from Mojang version manifests + Fabric/Forge maven);
  (b) integrate/steer an existing launcher and own only the mod-management half.
  Loadout-per-instance maps beautifully to MnemonicDB (versioned, diffable modpacks).
- **Identity note:** GPL-3.0 across the board (Vortex GPL-3, r2modman GPL-3 — code and
  format knowledge can legally flow in). CurseForge's API/ToS is the only licensing
  minefield — Modrinth-first is the safer default, exactly what Prism does.

**Suggested phasing (for analysis, not gospel):**
1. Mod-source abstraction + **Thunderstore/BepInEx** support (cheapest win, huge game
   coverage, proves the abstraction).
2. Generic BepInEx game family + Subnautica (bridges existing roadmap).
3. Modrinth support for Minecraft mods on existing instances.
4. Full Minecraft instance management + launching (the big one — separate design doc
   before committing).
5. "Import from …" wizards (Vortex, r2modman profiles, Prism instances) as adoption
   features.

**North star:** one Linux-first app where a user manages Skyrim, Lethal Company, and a
Minecraft modpack with the same Library, the same loadout/undo model, and the same UI.
Nobody has this. That's the fork's identity and moat.

---

## 8. Roadmap synthesis (updated)

1. ~~Baseline build/launch on CachyOS~~ ✅ (2026-07-06)
2. Commit patches; re-home fork (branch, remote, name/brand, Linux CI, README narrative).
3. Implement §6 (silent backup skip @ 5GB).
4. Finish Bannerlord end-to-end: manage → import Vortex archives → load order → Apply →
   launch (BLSE via protontricks or umu). Stardew as the low-friction second game.
5. **Own game-hashes pipeline** (§4.3) + vendor games.json — cut Nexus umbilicals.
6. Begin §7 phase 1 (mod-source abstraction, Thunderstore).
7. Subnautica/BepInEx game module (bridges §7 phase 2).
8. CachyOS/Arch diagnostics; protontricks→umu evaluation.
9. Community seeding (publish, architecture story, contributors).

## 9. Local reference paths

| What | Where |
| --- | --- |
| This fork | `~/Source/candidates/modding/NexusMods.App` |
| Brian's Vortex fork (source-run, formats reference) | `~/Source/Vortex` |
| Vortex live data (staging/downloads per game) | `~/.config/@vortex/main/` |
| nxmproxy (Rust nxm router, Linux port on `linux-port`, uncommitted) | `~/Source/candidates/modding/nxmproxy` |
| Candidate index | `~/Source/candidates/CANDIDATES.md` |
| App data / logs | `~/.local/share/NexusMods.App/` / `~/.local/state/NexusMods.App/Logs/` |

---

## 10. Session log — 2026-07-07 (Kiro)

### 10.0 Repo location correction
The **live fork** is now `~/Source/NexusMods.App`, branch **`linux-fork`**, remotes
`origin` → `github.com/Jeagermeister/NexusMods.App`, `upstream` → `Nexus-Mods/NexusMods.App`.
The `~/Source/candidates/modding/NexusMods.App` path referenced in §2/§9 is the original
bootstrap clone and is now **stale** (still on frozen `main` with the patches uncommitted).
Work continues in the `linux-fork` copy. Directives §6 and the two bootstrap patches are
already **committed** there (`c0e9e1be2`, `1c23f625e`).

### 10.1 Cut the Nexus umbilicals (roadmap step 5, "vendor" half — DONE)
Severed the fork's build-time and runtime dependencies on Nexus's dead infrastructure:

- **games.json** — was downloaded from `data.nexusmods.com` at build time. Now vendored at
  `src/NexusMods.Networking.NexusWebApi/Assets/games.json` (1.5 MB snapshot) and embedded
  directly; the `DownloadGamesJson`/`EmbedOutput` MSBuild targets were removed.
- **game-hashes DB** — was downloaded from `github.com/Nexus-Mods/game-hashes` (frozen
  2025-09-30) at build time. Now vendored at
  `src/NexusMods.Games.FileHashes/Assets/game_hashes_db.zip` (~21.7 MB, snapshot of release
  tag `vc2e27b8bf8632dca`) via **git-lfs**, embedded directly; the `DownloadHashesDatabase`/
  `EmbedOutput` targets were removed. `manifest.json` (127 B) is vendored alongside for
  reference. `.gitattributes` now LFS-tracks that zip path.
- **Runtime polling** — `FileHashesService` used to poll the frozen Nexus feed forever.
  Added `FileHashesServiceSettings.EnableRemoteUpdates` (**default `false`**). When off,
  `CheckForUpdateCore` uses the newest local DB or the embedded snapshot and makes **zero
  calls to Nexus infrastructure** at runtime.

Verified: `dotnet build` of both affected projects → 0 errors (43 pre-existing warnings).
Embed confirmed by output DLL sizes (FileHashes.dll ~22 MB, NexusWebApi.dll ~2.3 MB).

### 10.2 ⚠️ REMINDER — re-enable remote hash-DB updates once our pipeline is live
Per Brian's decision (2026-07-07), remote updates were disabled **on the condition we turn
them back on once settled**. When the fork's own game-hashes pipeline/feed exists
(roadmap step 5 "own game-hashes pipeline" + step 6):
1. Flip `FileHashesServiceSettings.EnableRemoteUpdates` default to `true`.
2. Repoint `GithubManifestUrl` / `GameHashesDbUrl` from `Nexus-Mods/game-hashes` to the
   fork-owned feed. (Both carry `TODO(linux-fork)` markers in the source.)

### 10.3 Environment / tooling notes
- git-lfs 3.7.1 installed on the CachyOS box (`git lfs install --local` run in this repo).
- Kiro terminal shell-integration currently doesn't return stdout (commands execute, exit
  codes correct); workaround is redirect-to-file + read. Not blocking.


---

## 11. Session log — 2026-07-07 (cont.) — Local game version recognition (WIP, Path B)

> Resume pointer: **start at task 3 below.** Tasks 1–2 are done and build clean; the whole
> approach is validated on real data. Nothing about this feature is wired into runtime yet.

### 11.1 Why (recap of the decision)
Roadmap step 5's "own game-hashes pipeline" was chosen to be **user-based (Path B), login-free,
and easy for any user** (Brian's call, 2026-07-07). Instead of a central CI pipeline that
re-downloads whole games from the Steam CDN, we recognise a user's *installed* game version
locally by hashing the files already on disk and verifying them against Steam's on-disk depot
manifests. No Steam login, no re-download. This fixes the "every game patched after Sept-2025
looks 100% modified" problem (root cause: frozen upstream hash DB — see §4.3).

### 11.2 The existing pipeline (already in-tree — DO NOT rebuild)
Two-stage: **producer** `steam app index -a <appId> -o <folder>/json`
(`src/NexusMods.Networking.Steam/CLI/Verbs.cs`) downloads+hashes files from the CDN and writes
a JSON tree (`hashes/`, `stores/steam/{apps,manifests}`); **builder** `game-hashes-db build`
(`src/NexusMods.Games.FileHashes/VerbImpls/BuildHashesDb.cs`) ingests that JSON tree +
`contrib/version_aliases.yaml` into a temp MnemonicDB store and zips it to `game_hashes_db.zip`
+ `manifest.json`. At runtime `FileHashesService` resolves an installed game's locator IDs
(Steam manifest IDs) to a `VersionDefinition`; no match ⇒ `GetGameFiles` returns nothing ⇒
synchronizer flags every file modified. Our local approach reuses the parsing/hashing pieces
but sources data from disk instead of the CDN.

### 11.3 VALIDATED on real data (high confidence)
- SteamKit2 **3.3.1** `DepotManifest.LoadFromFile(path)` (and `Deserialize(byte[]/Stream)`)
  parse Steam's `depotcache/<depotId>_<manifestId>.manifest` files directly. Filenames are
  stored **plaintext** (`FilenamesEncrypted == false`) — no depot key / login needed.
- Correctness proof: hashing the installed **Stardew Valley** files and comparing SHA1 to the
  cached manifest gave **3787/3787 real files matched, 0 mismatch, 0 missing** (27 directory
  entries skipped). Directory entries have `Chunks.Count == 0` — skip them.

### 11.4 Done (tasks 1–2) — built, 0 errors, but NOT yet referenced by anything
- `src/NexusMods.Networking.Steam/Local/LocalManifestReader.cs`
  `Manifest? TryReadManifest(AbsolutePath depotCacheDir, DepotId, ManifestId)` — finds
  `<depot>_<manifest>.manifest`, `DepotManifest.LoadFromFile` → `ManifestParser.Parse` → app
  `Manifest` DTO. Returns null if missing / filenames encrypted / parse fails.
  Static helper `ManifestFileName(depotId, manifestId)`.
- `src/NexusMods.Networking.Steam/Local/LocalFileHasher.cs`
  `Task<ManifestVerificationResult> VerifyAndHashAsync(AbsolutePath gameDir, Manifest, IProgress<double>?, ct)`.
  Skips dir entries, sanitizes paths via `RelativePath.FromUnsanitizedInput` (Windows-depot
  backslashes), hashes each file with `MultiHasher.HashStream`, compares `.Sha1` to
  `Manifest.FileData.Hash`. Returns `VerifiedFile{ RelativePath Path; MultiHash Hash }[]` +
  `MissingCount`/`MismatchCount`/`TotalFiles`/`MatchedCount`/`IsFullyVerified`.

### 11.5 REMAINING WORK — resume here

**Task 3 (NEXT, the invasive core): writable overlay hash DB + union into `FileHashesService`.**
Decision FINAL: **overlay-union**. Keep the shipped/embedded DB read-only (it has all upstream
games). Add a separate *writable* MnemonicDB overlay at `_hashDatabaseLocation / "local-overlay"`
that locally-recognised versions are written into, and union it into the read paths of
`src/NexusMods.Games.FileHashes/FileHashesService.cs`:
`GetGameFiles`, `TryGetGameVersionDefinition`, `GetVersionDefinitions`
(`SuggestVersionData` / `GetKnownVanityVersions` / `TryGetLocatorIdsForVanityVersion` are covered
transitively). Rejected "augment the embedded DB in place" (RocksDB read-only vs read-write lock
conflict + additions lost on re-extract).
- Connection setup references: `FileHashesService.OpenDb` (read-only: `new Connection(logger,
  store, provider, [], readOnlyMode: true, prefix: "hashes", queryEngine: _queryEngine)`) and
  `BuildHashesDb` ctor (writable: `new DatomStore(...RocksDbBackend.Backend())` + `new
  Connection(logger, datomStore, provider, [])`). Overlay likely needs the SAME `prefix: "hashes"`
  and the FileHashes query engine so model attributes align — VERIFY this.
- Model write API (from `BuildHashesDb`): `HashRelation.New(tx){XxHash3,XxHash64,MinimalHash,Md5,
  Sha1,Crc32,Size}`, `PathHashRelation.New(tx){Path,HashId}`, `SteamManifest.New(tx){AppId,DepotId,
  ManifestId,Name,FilesIds}`, `VersionDefinition.New(tx){Name,OperatingSystem,GameId,GOG,Steam,
  EpicBuildIds}` then `tx.Add(versionDef, VersionDefinition.SteamManifestsIds, manifest.Id)`.
- After writing, `FileHashesService` needs to reload/reopen so the new datoms are visible
  (it caches `_currentDb`). Add a reload path; watch the read-only handle vs the writable overlay.
- Watch out: the app's DB is opened via `IHostedService.StartAsync`; overlay should be opened
  alongside and disposed in `Dispose`.

**Task 4: CLI verb to run indexer end-to-end + verify.** Reorder note: a CLI verify/report verb
was going to be done before task 3 but we paused first — do it with task 3. Simplest: a verb in
`NexusMods.Networking.Steam.CLI/Verbs.cs` taking explicit `--game <dir> --depotcache <dir>
--depot <id> --manifest <id>` (avoids adding a GameFinder dependency to that project) that runs
reader+hasher+overlay-writer and prints stats. Test with **Stardew (appid 413150, depot 413153,
manifest 6005910083361727734, install `/mnt/17b46faa-30cb-4981-9c89-6428a12cd225/SteamLibrary/
steamapps/common/Stardew Valley`)**, then **Bannerlord (appid 261550, depots 261551/261552)**.
Confirm the game is no longer "100% modified" after indexing.

**Task 5: in-app one-click "Recognize installed version" UX.** When a managed game's version is
unknown, offer a one-click action that runs the local indexer with progress. The app already has
the needed inputs via the Steam locator: `SteamLocator.cs` uses GameFinder `SteamHandler`,
`gameFinderGame.AppManifest.InstalledDepots` → (depotId key, `x.Value.ManifestId`), `.Path`
(install dir), `.SteamPath` (root → `SteamPath/depotcache`). `GameLocatorResult.LocatorIds`
already carries the installed manifest IDs. Include Steam "Verify integrity" guidance for
modified installs (SHA1 check protects correctness regardless).

**Task 6: build-verify, update this doc, commit.**

### 11.6 Key facts / gotchas
- depotcache is GLOBAL at `<SteamRoot>/depotcache` (e.g. `/home/sirjeager/.local/share/Steam/
  depotcache`), even for games installed on the `/mnt/17b46faa-...` library.
- Manifest file name convention: `<depotId>_<manifestId>.manifest`.
- `MultiHasher.HashStream(Stream)` (`NexusMods.Sdk.Hashes`) needs a seekable stream (sets
  Position=0, reads Length). `AbsolutePath.Read()` returns an async-disposable seekable stream.
  `Size.Value` is `ulong`.
- Live fork: `~/Source/NexusMods.App`, branch `linux-fork`. `EnableRemoteUpdates=false` (§10.2)
  so runtime never contacts Nexus; the local overlay is how versions get added now.
- **Tool/environment quirks this session (may or may not persist):** the Kiro terminal did not
  return stdout — every shell command had to redirect to a file which was then read. And the
  multi-file `read_files` tool returned empty; single-file `read_file` worked. If these recur,
  use the same workarounds.

### 11.7 Git state at pause
Foundation classes (11.4) + this doc committed as a WIP commit on `linux-fork`. Umbilical work
(§10) is at `264dcd73c` and pushed to `origin` (Jeagermeister/NexusMods.App).

---

## 12. Session log — 2026-07-07 (cont.) — Local recognition wired into runtime (§11 tasks 3–6 DONE)

> Completes §11's remaining work. The overlay is now unioned into `FileHashesService` and driven
> by a working CLI verb, validated on real Stardew + Bannerlord data. The only §11 item left is
> the in-app one-click UX (was §11.5 "Task 5"), re-tracked as §12.5 below.

### 12.1 Writable overlay + union reads (the invasive core — DONE)
`src/NexusMods.Games.FileHashes/FileHashesService.cs`:
- New `OverlayDb(Connection, DatomStore, Backend)` record + `_overlayDb` field. `EnsureOverlayOpen()`
  opens a **writable** RocksDB at `_hashDatabaseLocation / "local-overlay"` with
  `new Backend()` + `new Connection(logger, store, _provider, [], prefix: "hashes", queryEngine: _queryEngine)`.
  The `prefix:"hashes"` + shared query engine on a *writable* connection is exactly what
  `tests/.../StubbedFileHasherService.cs` does — that resolved §11.5's "VERIFY prefix" open question.
- `ReadDbs()` yields the shipped DB (`_currentDb.Db`) then the overlay (`_overlayDb.Connection.Db`).
  Every read path now unions over it: `GetGameFiles` (Steam/EGS resolved per-id, first DB wins to
  avoid dupes; GOG factored into a per-DB `GetGogGameFiles` helper), `TryGetGameVersionDefinition`
  (split into a per-DB `...InDb(out def, out score)` — version↔manifest matching **must** stay
  within one DB because entity ids are DB-scoped — the wrapper keeps the highest-scoring match,
  shipped DB preferred on ties), `GetVersionDefinitions`, `TryGetLocatorIdsForVanityVersion`.
  `SuggestVersionData` / `GetKnownVanityVersions` inherit the union transitively.
- No `_currentDb`-style reload needed for the overlay: reads take `Connection.Db` fresh each call,
  so writes are visible immediately in-process. `StartAsync` opens the overlay (guarded — failure
  is logged, never blocks the shipped DB); `Dispose` disposes overlay Store + Backend.
- Write API: `AddLocalSteamVersionAsync(appId, depotId, manifestId, versionName, NexusModsGameId? gameId,
  OperatingSystem os, IReadOnlyList<(RelativePath, MultiHash)> verifiedFiles, ct)` — added to
  `IFileHashesService`. Writes a `HashRelation` + `PathHashRelation` per verified file, a
  `SteamManifest`, and (when `gameId` is set) a `VersionDefinition` anchored via
  `tx.Add(versionDef, VersionDefinition.SteamManifestsIds, manifest.Id)`. Single transaction;
  same-tx temp-id references (`HashId = hashRelation` new-obj, `FilesIds = pathIds`,
  `SteamManifestsIds = manifest.Id`) commit fine (confirmed at runtime).
- `StubbedFileHasherService` got a no-op implementation of the new interface member.

### 12.2 CLI verb `steam local-index` (DONE)
`src/NexusMods.Networking.Steam/CLI/Verbs.cs`, registered in `AddSteamVerbs`. Options:
`-g/--game` (install dir), `-c/--depotcache`, `-d/--depot`, `-m/--manifest` (required);
`-a/--app`, `-i/--gameId` (Nexus id; when set, writes a version definition), `-n/--versionName`,
`-s/--os` (optional). Flow: `LocalManifestReader.TryReadManifest` → `LocalFileHasher.VerifyAndHashAsync`
→ `IFileHashesService.AddLocalSteamVersionAsync`, then prints verification stats and re-queries
`GetGameFiles` to confirm recognition. Wiring: `NexusMods.Networking.Steam.csproj` now references
`NexusMods.Abstractions.Games.FileHashes`; `AddSteam()` registers `LocalManifestReader` +
`LocalFileHasher` as singletons.

Run it headless in-process with the `as-main` startup prefix (no UI, no proxy), e.g.:
```
dotnet src/NexusMods.App/bin/Debug/net9.0/NexusMods.App.dll as-main steam local-index \
  -g "<install dir>" -c "$HOME/.local/share/Steam/depotcache" -d <depotId> -m <manifestId> \
  -a <appId> -i <nexusGameId>
```

### 12.3 Validated on real data (2026-07-07)
- **Stardew** (depot 413153, manifest 6005910083361727734, app 413150, gameId 1303):
  **3787/3787 matched, 0 missing, 0 modified**; `GetGameFiles` then returned **3787** files.
- **Bannerlord** depot 261552 (manifest 7719431349712662189, app 261550, ~5 GB):
  **4938/4938 matched, 0 missing, 0 modified**; `GetGameFiles` then returned **4938** files.
  The Bannerlord run reopened the overlay that already held the Stardew data and appended
  cleanly → confirms cross-process persistence and no RocksDB single-writer lock issue.
- Bannerlord's full install is ~91–101 GB across depots **261551 (~64 GB)** / 261552 (~5 GB)
  + two DLC depots (2240111 ~1.2 GB, 2927200 ~31.5 GB — **their manifests are NOT in depotcache**,
  so those can't be locally recognised yet). Only 261551/261552 have cached manifests. The 64 GB
  depot 261551 was **not** hashed here (≈40 min disk-bound job); the mechanism is size-agnostic and
  proven, so 261552 stands in as the real-game validation. To fully de-modify Bannerlord, index
  261551 too (same command, `-d 261551 -m 813532240231811649`).
- Full solution build after all changes: **0 errors** (submodules initialised: SMAPI + docs/Nexus).

### 12.4 Files changed this session (all on `linux-fork`, uncommitted at time of writing → see 12.6)
- `src/NexusMods.Abstractions.Games.FileHashes/IFileHashesService.cs` (+`AddLocalSteamVersionAsync`)
- `src/NexusMods.Games.FileHashes/FileHashesService.cs` (overlay + union reads + writer)
- `src/NexusMods.Networking.Steam/CLI/Verbs.cs` (`steam local-index` verb)
- `src/NexusMods.Networking.Steam/Services.cs` (register reader/hasher)
- `src/NexusMods.Networking.Steam/NexusMods.Networking.Steam.csproj` (ref Abstractions.Games.FileHashes)
- `tests/NexusMods.StandardGameLocators.TestHelpers/StubbedFileHasherService.cs` (no-op impl)

### 12.5 REMAINING — in-app one-click "Recognize installed version" UX (was §11.5 Task 5)
The runtime plumbing is done; the UX is not. When a managed game's version is unknown, surface a
one-click action that calls `IFileHashesService.AddLocalSteamVersionAsync` with progress, using the
Steam locator's already-available inputs (`SteamLocator.cs`: `AppManifest.InstalledDepots` →
(depotId, ManifestId), install `.Path`, `.SteamPath`→`/depotcache`; `GameLocatorResult.LocatorIds`
carries the installed manifest IDs). Iterate **all** installed depots that have cached manifests.
Show Steam "Verify integrity" guidance for modified installs (the SHA1 check keeps recorded data
correct regardless). **Known refinement to fold in:** the write path is not idempotent — re-indexing
the same manifest appends a second `SteamManifest` for that id. `GetGameFiles` (Steam) breaks on the
first match so it's harmless today, but the UX (and ideally the writer) should dedupe/replace an
existing overlay manifest by `ManifestId` before writing.

### 12.6 Notes / environment
- The writable overlay lives at `~/.local/share/NexusMods.App/FileHashesDatabase/local-overlay`
  and now contains the Stardew + Bannerlord(261552) test entries — legitimate runtime recognition
  data (not part of the repo). Do **not** run the CLI verb while the main app is running (RocksDB
  single writer; one process at a time).
- Kiro terminal stdout-capture quirk from §10.3/§11.6 **persists**; still worked around with
  redirect-to-file. The multi-file `read_files` tool worked fine this session.
- Full solution build requires the submodules — run `git submodule update --init --recursive` once
  (done this session: SMAPI @ `fd73446`, docs/Nexus @ `fe4e8b1`).

---

## 13. Session log — 2026-07-07 (cont.) — Recognition service, idempotency, tests (follow-up "A1–A3")

> Builds on §12. Turns the recognition pipeline into a reusable, idempotent service driven from a
> `GameInstallation`, exposes it as a one-command CLI action, and adds regression tests. The GUI
> button (§12.5) is intentionally still open — see 13.5.

### 13.1 Idempotency (A2 — DONE)
`AddLocalSteamVersionAsync` now returns early if a `SteamManifest` with the same `ManifestId` is
already in the overlay (`SteamManifest.FindByManifestId(overlay.Db, manifestId).Any()`). A manifest id
uniquely identifies a depot content snapshot, so re-recognition is a genuine no-op — no duplicate
manifests/version definitions stack up. Verified at runtime ("already recorded in the overlay; skipping").

### 13.2 Reusable recognition service (A1 core — DONE)
`ILocalGameVersionRecognizer` / `LocalGameVersionRecognizer` (`NexusMods.Networking.Steam/Local`,
registered in `AddSteam()`):
- `CanRecognize(GameInstallation)` → true for Steam installs that expose a depotcache path.
- `RecognizeAsync(GameInstallation, IProgress<double>?, ct)` resolves everything from the installation:
  install dir = `LocatorResult.Path`, appId = `LocatorResult.StoreIdentifier`, installed manifest ids =
  `LocatorResult.LocatorIds`, gameId = `Game.NexusModsGameId`, OS via `LocatorResult.TargetOS.MatchPlatform`.
  It iterates every installed depot, reads its cached manifest, hashes+verifies, and records the
  verified-vanilla files. It writes ONE version definition for the whole game (on the first recognised
  depot, named "<Game> (local)") and only the file manifest for the rest. Returns a `LocalRecognitionResult`
  (depots recorded / skipped-no-manifest, verified / missing / modified counts) and reports 0..1 progress.

Supporting changes:
- **`GameLocatorResult.SteamDepotCachePath`** (new, nullable) — the Steam client's global depotcache
  directory. It cannot be derived from the per-library install path, so `SteamLocator` now captures it
  as `gameFinderGame.SteamPath.Combine("depotcache")`.
- **`LocalManifestReader.TryReadManifestByManifestId` / `TryFindManifestFile`** — the locator exposes
  manifest ids but not depot ids, so the cached `<depotId>_<manifestId>.manifest` file is found by
  matching the manifest-id suffix and the depot id is recovered from the file name.

### 13.3 One-command in-app surface (A1 — DONE)
`steam recognize-game -a <appId>` (`NexusMods.Networking.Steam/CLI/Verbs.cs`) resolves the installed,
matching Steam `GameInstallation`(s) via `IGameRegistry.LocateGameInstallations()` and runs the service
across all installed depots. This is the concrete, verifiable "recognise an installed game in one action"
entry point (the earlier `steam local-index` remains for single-depot / explicit-path use).

### 13.4 Tests (A3 — DONE)
`tests/Networking/NexusMods.Networking.Steam.Tests/LocalManifestReaderTests.cs` — 3 passing tests for
`TryFindManifestFile`: recovers the depot id from the cached file name, returns false when the requested
manifest is absent, and returns false when the depotcache directory is missing. (A full `FileHashesService`
overlay-union integration test was deliberately not added: it requires standing up the entire
MnemonicDB/query-engine/settings graph, disproportionate to its value given the overlay union + idempotency
are already validated end-to-end on real data — §12.3 and below.)

### 13.5 STILL OPEN — the GUI button (was §12.5)
The reusable service is done and injectable app-wide, but there is still **no GUI button**. Findings that
shaped this:
- Diagnostics **cannot** trigger in-app actions (their markdown links only open external URIs via
  `osInterop`; `NoWayToSourceFilesOnDisk` is disabled with a TODO about needing a UI backup path). So the
  action must be a real button/menu item, not a diagnostic link.
- `NexusMods.App.UI` does **not** reference `NexusMods.Networking.Steam` or `Abstractions.Games.FileHashes`,
  so wiring a button needs either an interface relocation (e.g. move `ILocalGameVersionRecognizer` to an
  abstraction the UI already references) or a new project reference, plus an Avalonia view/VM wired into a
  page (the `ApplyControlViewModel` + `IJobMonitor` pattern is the template) and localization strings.
- It also can't be runtime-verified in this headless environment, so it was scoped out of this PR rather
  than shipped unverified. Recommended next step: relocate the recognizer interface to an abstraction, add
  a "Recognize installed version" action to the loadout left menu (mirroring `ApplyControlViewModel`),
  run it as an `IJobDefinition` for progress, and trigger it when `SuggestVersionData` reports an unknown
  version.

### 13.6 Validation this round
- `steam recognize-game -a 413150` (Stardew): located the install, derived depotcache from the new locator
  field, found the manifest by id, hashed 3787 files, and hit the idempotency skip (manifest already
  recorded) — confirming the full `GameInstallation`-driven path and A2.
- `LocalManifestReaderTests`: 3/3 passed. Full solution build: 0 errors.

### 13.7 Files changed this round
- `src/NexusMods.Sdk/Games/Locators/GameLocatorResult.cs` (+`SteamDepotCachePath`)
- `src/NexusMods.Backend/Games/Locators/SteamLocator.cs` (capture depotcache)
- `src/NexusMods.Networking.Steam/Local/LocalManifestReader.cs` (`TryReadManifestByManifestId`/`TryFindManifestFile`)
- `src/NexusMods.Networking.Steam/Local/LocalGameVersionRecognizer.cs` (new service)
- `src/NexusMods.Networking.Steam/Services.cs` (register service)
- `src/NexusMods.Networking.Steam/CLI/Verbs.cs` (`steam recognize-game`)
- `src/NexusMods.Games.FileHashes/FileHashesService.cs` (idempotency guard)
- `tests/Networking/NexusMods.Networking.Steam.Tests/LocalManifestReaderTests.cs` (new tests)
