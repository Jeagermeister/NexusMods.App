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

---

## 14. Session log — 2026-07-08 — In-app "Recognize installed version" GUI (finishes §12.5/§13.5)

> IMPORTANT: PR #2 merged **only the first commit** (`8b8a6e554`); the follow-up commit
> `e96baef45` (recognizer service, idempotency, `recognize-game` verb, `SteamDepotCachePath`,
> tests) was **not** in the merge. This session's branch `feature/recognize-installed-version-ui`
> **cherry-picks that stranded commit** (as `2ef4e9159`) and adds the GUI on top. PR #3 therefore
> carries both. If reviewing history: the backend changes here are the §13 work re-applied.

### 14.1 Interface relocation (so the UI can consume it)
`ILocalGameVersionRecognizer` + `LocalRecognitionResult` moved from `NexusMods.Networking.Steam.Local`
to `src/NexusMods.Abstractions.Games.FileHashes/ILocalGameVersionRecognizer.cs`. The impl
(`LocalGameVersionRecognizer`) stays in `NexusMods.Networking.Steam` and now implements the abstraction.
`NexusMods.App.UI` reaches this abstraction **transitively via `NexusMods.Collections`**, so no new UI
project reference was needed. DI registration in `Networking.Steam/Services.cs` updated to the new
fully-qualified interface name.

### 14.2 The GUI action (extends ApplyControl — deliberately low-surface)
Rather than a new control quartet + page wiring (many unverifiable files), the action was added to the
existing, already-registered/rendered `ApplyControl` in the loadout footer:
- `IApplyControlViewModel`: `+ RecognizeVersionCommand`, `+ IsVersionUnknown`, `+ IsRecognizingVersion`.
- `ApplyControlViewModel`: resolves `ILocalGameVersionRecognizer` (optional — `GetService`, null-guarded)
  and `IFileHashesService`, loads the loadout's `GameInstallation` (`loadout.InstallationInstance`).
  `IsVersionUnknown = recognizer.CanRecognize(install) && !TryGetVanityVersion((Store, LocatorIds), out _)`
  (wrapped in try/catch — never throws). `RecognizeVersionCommand` runs `RecognizeAsync` off the UI thread,
  re-evaluates unknown state, and shows a Success/Neutral/Failure toast with the recorded depot/file counts.
- `ApplyControlView.axaml` / `.axaml.cs`: a "Recognize installed version" button (default `IsVisible=False`,
  bound to `IsVersionUnknown`) + a "Recognizing…" spinner row (bound to `IsRecognizingVersion`), bound via
  the existing code-behind `BindCommand`/`OneWayBind` pattern.
- `ApplyControlDesignViewModel`: stub members added.
- Strings are inline literals (not localized) to keep the change small; **follow-up: move to `Language.resx`**.

### 14.3 Status / verification
- Full solution build: **0 errors**. Steam tests: **3/3 pass**.
- ⚠️ **Not runtime-verified.** Avalonia UI can't be exercised headlessly here, so the button's rendering,
  visibility gating, and toast behaviour need a manual launch to confirm. The backend it calls is validated
  (§12.3/§13.6). Logic reviewed: DI is null-guarded, the version check can't throw, visibility defaults to
  hidden so known games (the common case) show nothing.
- Behavioural expectation: the button appears in the loadout footer only for Steam games whose version isn't
  in the hash DB/overlay; clicking it recognises all installed depots and (on success) the button disappears
  as the version becomes known.

### 14.4 Files changed
- `src/NexusMods.Abstractions.Games.FileHashes/ILocalGameVersionRecognizer.cs` (new — relocated interface + result)
- `src/NexusMods.Networking.Steam/Local/LocalGameVersionRecognizer.cs` (impl now implements the abstraction)
- `src/NexusMods.Networking.Steam/Services.cs` (DI registration updated)
- `src/NexusMods.App.UI/LeftMenu/Items/ApplyControl/{IApplyControlViewModel,ApplyControlViewModel,ApplyControlDesignViewModel,ApplyControlView.axaml,ApplyControlView.axaml.cs}`

### 14.5 Remaining follow-ups
- Localize the UI strings (§14.2).
- Optional: a determinate progress bar (the recognizer already reports `IProgress<double>`; currently only an
  indeterminate spinner is shown).
- Manual runtime verification of the button on a real unknown-version Steam game.

---

## 15. Session log — 2026-07-08 (Claude Code / Fable 5) — Review-driven hardening pass

> A four-agent adversarial review of the full fork delta (upstream `48284f38c`..`linux-fork`) was run,
> then the confirmed findings were fixed on branch `fix/review-findings`. Full solution builds with
> 0 errors; synchronizer (12), DataModel (232), and Steam (11+2 new suites) tests all pass.

### 15.1 CRITICAL — backup skip no longer allows deletion of unrestorable files
The §6 silent backup skip had a traced data-loss path: manage an unknown-version game >5GB (backup
skipped) → deactivate/unmanage → desired state is empty (unknown version = no known game files) →
every file maps to `BackupFile|DeleteFromDisk` → backup skips again but the deletes still ran, wiping
the whole game folder unrecoverably. `ActionBackupNewFiles` now (a) backs up the **smallest files
first up to the 5GB cap** (protecting small genuinely-modified configs, an option §6 explicitly
allowed) instead of all-or-nothing, and (b) **downgrades pure deletions** (DeleteFromDisk without
ExtractToDisk) of files it could not back up to "leave on disk", by clearing the action flag before
`ActionDeleteFromDisk` runs. Overwrites (delete+extract) proceed — that loss is the accepted §6
tradeoff. Log raised to Warning.

### 15.2 Recognition correctness fixes (FileHashesService + recognizer)
- **Idempotency guard no longer drops the VersionDefinition.** If a manifest was first recorded
  without a game id (`steam local-index` without `-i`), a later call with a game id now creates the
  missing definition anchored to the existing manifest instead of silently skipping — previously the
  UI toasted success while the game stayed "unknown version".
- **Stale definitions are superseded.** Writing a definition retracts any previous overlay definition
  with the same GameId+Name, so re-recognition after a game update can't leave name-based lookups
  (`TryGetLocatorIdsForVanityVersion`, vanity version lists) resolving to stale manifest ids. The old
  manifest file data stays (still valid hash data).
- **Newer embedded DB is adopted after an app upgrade.** With `EnableRemoteUpdates=false`, a local DB
  now only wins over the embedded snapshot if it is at least as new (`ApplicationConstants.BuildDate`);
  previously a refreshed vendored `game_hashes_db.zip` was never extracted for existing installs.
  (Debug builds: embedded time is UnixEpoch, so local always wins in dev; `forceUpdate` re-extracts.)
- **`EnsureOverlayOpen` is now locked** (StartAsync raced writers) and disposes the half-opened
  store/backend on failure (previously a partial open held the RocksDB LOCK for the session).
- New query `IFileHashesService.TryGetLocalSteamManifestStatus(manifestId, gameId, out hasDefinition)`
  lets callers check the overlay without writing. Stub updated.

### 15.3 Recognition robustness + performance (LocalFileHasher / recognizer)
- **One unreadable file no longer aborts a whole run** (was: 40 min of hashing discarded on a single
  permission-denied file). Per-file IO errors are caught and counted in the new
  `ManifestVerificationResult.UnreadableCount`; `IsFullyVerified` accounts for it.
- **Hashing is now parallel (min(cores,8)) with 1MiB buffered reads** (MultiHasher reads 8KiB chunks),
  and files whose **on-disk size differs from the manifest are skipped without hashing** — a large win
  on modded installs. Progress reporting is monotonic under parallelism.
- **Already-recorded depots skip before hashing, not after** (re-running `recognize-game` on an
  indexed game is now sub-second instead of a full re-hash; verified live on Stardew). Progress across
  depots is **byte-weighted** (was depot-count-weighted: a 60GB depot sat at 0% while language depots
  each counted the same), via a synchronous inline IProgress (Progress<T> posts async and could move
  the bar backwards).
- Manifest paths that escape the game directory (`..`) are rejected; CLI ids are range-validated
  (silent `(uint)` wrap fixed); `recognize-game` exits non-zero when nothing was recorded.

### 15.4 UI fixes (ApplyControl)
- **`IsVersionUnknown` is now computed in `WhenActivated` after `GetFileHashesDb()`** — previously it
  was computed in the constructor before the DB loaded, so the button appeared for known games (a
  lookup on an unloaded DB reports everything unknown), and it was never re-evaluated on activation
  (stale after CLI/other-loadout recognition).
- Recognition is cancellable: closing the loadout view cancels the run (CTS wired via WhenActivated).
- Failures are logged (previously swallowed with only a toast); "Recognise/Recognize" spelling unified.

### 15.5 Build guard
`NexusMods.Games.FileHashes.csproj` now fails fast (incremental target) if `Assets/game_hashes_db.zip`
is a git-lfs pointer file instead of the real database — previously a clone without git-lfs built
fine and died only at runtime. Verified both directions (real zip passes, pointer errors).

### 15.6 New tests
`LocalFileHasherTests` (6): matched/modified/missing classification + backslash sanitization, size
pre-check, unreadable-file containment, `..` traversal rejection, monotonic progress ending at 1.0,
empty manifest. `LocalManifestReaderTests` (+2): suffix near-miss (`999_9123` must not match id 123),
non-numeric depot prefixes.

### 15.7 Review findings NOT fixed this session (deliberate)
- Overlay extraction into a `LocalHashOverlay` collaborator (worthwhile refactor; the locking fix
  landed without it).
- GOG overlay union has no dedup (harmless until GOG data can be written; revisit with the
  store-agnostic recognizer).
- UI strings still not localized; spinner still indeterminate (byte-weighted IProgress now makes a
  determinate bar easy).
- `Dispose` vs in-flight readers race (pre-existing upstream flaw, mirrored by the overlay).

---

## 16. Session log — 2026-07-08 (cont.) — GUI verification round-trip + recognition-as-job

> Brian manually exercised the "Recognize installed version" button (first-ever GUI run). The test
> validated both visibility cases and exposed two real mid-run design flaws, fixed on branch
> `fix/recognition-ux`. Also: CS8785 root-caused and eliminated.

### 16.1 What the manual test showed
- **Bannerlord (unknown version): button appeared and worked** — clicking recorded the 5GB depot
  (4938/4938 matched) in seconds.
- **Stardew (known version): button correctly absent.** Stardew's last update predates the frozen
  hash DB snapshot (2025-09-30), so its version resolves from the shipped database — the negative
  case behaves as designed. (Verify casually: its game card shows a version string.)
- **Flaw 1:** the version definition was written after the FIRST depot completed, so the game read
  "known" while the 64GB depot was still hashing (button vanished early, partial data state).
- **Flaw 2:** navigating away deactivated the view and CANCELLED the in-flight run (the §15 CTS
  wiring was too aggressive) — no toast, no completion, half-recognized game.

### 16.2 Fixes (branch `fix/recognition-ux`)
- **Definition-at-end:** `RecognizeAsync` now records per-depot file manifests with no game id and
  writes the single `VersionDefinition` only after the whole run completes. A cancelled/failed run
  leaves the version unknown → the button returns → re-running **resumes** (pass-1 skip makes
  completed depots instant). Cancellation-safe by construction.
- **Recognition runs as a job:** new `RecognizeGameVersionJob` (`Abstractions.Games.FileHashes`) +
  `ILocalGameVersionRecognizer.RecognizeInBackground(installation)` — starts via `IJobMonitor.Begin`,
  reports byte-weighted progress through `ctx.SetPercent`, survives navigation, and dedupes: a second
  call for the same install path joins the in-flight job. The ApplyControl VM observes
  `HasActiveJob<RecognizeGameVersionJob>` for its running state (any view of the same game shows it)
  and re-evaluates `IsVersionUnknown` when a run ends.
- **UI:** the "Recognizing…" row now shows live percentage from the job (`ObservableProgress`); the
  button yields to the progress row while running; spinner fixed to 20×20 (was a squished 12×24).
- **CS8785 eliminated (§6 warm-up item):** the `GenerateWeaveSources` crash came from the **Weave
  2.1.0** analyzer dragged in transitively by `NexusMods.MnemonicDB.SourceGenerator` (NuGet ignores
  `exclude="Analyzers"` on transitive deps); it unconditionally reads a compiler-visible property
  that only Weave's own — never imported — targets declare → `Path.Combine(null)`. The repo has no
  `.weave` files, so a new root `Directory.Build.targets` strips the analyzer after
  `ResolveLockFileAnalyzers`. Clean rebuild: **226 → 182 warnings, 0 errors**. (Upstream MnemonicDB
  could take this as an issue/PR.)

### 16.3 Bannerlord fully recognized + the perf number
Headless `steam recognize-game -a 261550` after restoring the overlay:
- Depot 261551 (the big one, previously estimated "~40 min"): **17,067/17,067 matched in ~23 s**
  (parallel hashing × 1MiB buffers; ~2.5 CPU-minutes across cores).
- The two DLC depots' manifests are now present in depotcache (they weren't in §12.3) — both
  recognized (307 + 3,109 files). **Whole game: 4/4 depots, 20,483 files, 0 modified, 36 s total.**
- Pass-1 skip + definition-at-end validated live: already-recorded depot skipped instantly, no
  duplicate definition written.

### 16.4 State / follow-ups
- Overlay at `~/.local/share/NexusMods.App/FileHashesDatabase/local-overlay` now holds: Stardew
  (413153), Bannerlord 261551/261552 + both DLC depots, one "(local)" definition per game.
- Follow-ups that remain: UI string localization (Language.resx); GUI re-verification of the new
  job-based flow (progress %, button↔row swap, completion toast — the toast has still never been
  seen on screen); consider a cancel affordance on the progress row (jobs support Cancel);
  remaining 182 warnings are upstream CS0618/CS0219-class noise, mechanical to clear.

---

## 17. Session log — 2026-07-08 (cont., Claude Code / Fable 5) — Mod-source Phase 1: Thunderstore plumbing (PR A)

> Directive §7 Phase 1 begins. Design-doc-first per FABLE5-TASKS §2.1: `DESIGN-modsources.md`
> (repo root) is the reviewed design; Brian approved it + its four §14 decisions this session
> (pilot game **Risk of Rain 2**, debug-only feature gate, ecosystem-schema vendoring deferred,
> keep `NexusMods.*` naming). Work is on branch `feature/thunderstore-source` (PR A of four).

### 17.0 Repo repair first — PR #5's merge had been clobbered
The 2026-07-08 history scrub (see memory/§16 era) force-pushed `linux-fork` from a local state
that predated Brian's 08:30 merge of PR #5 — GitHub kept the PR marked "merged" but the
recognition-as-job commits were no longer on the branch. Verified scrubbed↔pre-scrub trees
byte-identical, re-merged scrubbed `fix/recognition-ux` (4d54cc3b6) as `d6f743918` with
GitHub's original merge message, pushed. Build on restored tip: 0 errors / 182 warnings (§16
baseline). **Lesson: after a filter-branch scrub, check no merges landed between the local
snapshot and the force-push.**

### 17.1 Research (four parallel agents, condensed into the design doc §2)
Codebase: the core is already source-agnostic (`ILibraryService.AddDownload` takes any
`IDownloadJob`; `AddDownloadJob` → `AddMetadata(tx, libraryFile)` is the single identity
choke point; installers + `ILoadoutManager.InstallItem` don't care about origin; protocol
dispatch iterates `IIpcProtocolHandler`s by scheme). Nexus privilege lives in:
`NxmIpcProtocolHandler` (monolith), `HandlerRegistration` (hardcoded `"nxm"`),
`DownloadsService` (observes only `INexusModsDownloadJob`, `DownloadInfo.GameId` is
`NexusModsGameId` — the one non-additive refactor, deferred to PR C), `LibraryItemRemovalInfo`
(redownloadable==Nexus), `GameRegistry.TryGetMetadata` (needs `NexusModsGameId`; in-code TODO).
Thunderstore (live-verified 2026-07-08): experimental API is anonymous/CORS-open; deps are
exact-version strings `Namespace-Name-1.2.3`; download endpoint 302s to CDN zip;
`ror2mm://v1/install/thunderstore.io/{ns}/{name}/{version}/` one-click; **ecosystem schema**
`/api/experimental/schema/dev/latest/` (296 games, 88 loader packages, per-game installRules —
r2modman consumes this instead of hardcoding; feeds Phase 2). Corrections: r2modmanPlus is
**MIT** (not GPL-3); no `thunderstore://` scheme exists; no published rate limits.

### 17.2 PR A implemented (purely additive; app behavior unchanged unless verbs invoked)
- **`src/NexusMods.Abstractions.Thunderstore/`** — `PackageRef`/`PackageVersionRef`
  (right-anchored dependency-string parsing: names/versions have restricted charsets,
  namespaces may contain dashes), DTOs for the experimental API, `IThunderstoreApiClient`,
  `IThunderstoreLibrary`, `IThunderstoreDownloadJob`, and MnemonicDB models mirroring the
  Nexus triple: `ThunderstorePackageMetadata` (≙ mod page; FullName indexed) /
  `ThunderstoreVersionMetadata` (≙ file; FullName indexed, `Dependencies` StringsAttribute
  verbatim) / `ThunderstoreLibraryItem [Include<LibraryItem>]`. No game/community ref on the
  package — packages are global on Thunderstore; the target game is an install-time property.
- **`src/NexusMods.Networking.Thunderstore/`** — `ThunderstoreApiClient` (anonymous GETs,
  404→null), `ThunderstoreUrls` (stable URL shapes), `ThunderstoreLibrary`
  (GetOrAddVersion/GetLatestVersion/IsAlreadyDownloaded/CreateDownloadJob),
  `ThunderstoreDownloadJob : HttpDownloadJob` (ExternalDownloadJob subclass pattern; pause/
  resume native; `AddMetadata` stamps `ThunderstoreLibraryItem` + `DownloadedFile`),
  `ThunderstoreDependencyResolver` (BFS over exact-version strings, semver-max on conflicts,
  cycle-safe, 512-package tripwire, errors collected not thrown), CLI verbs
  `thunderstore resolve` / `thunderstore download` (`-p ns-Name [-v x.y.z] [-n/--noDeps]`).
- Wiring: `.AddThunderstore()` in `src/NexusMods.App/Services.cs` (next to `AddSteamCli`);
  3 new projects in the sln (src/Abstractions, src/Networking, tests/Networking folders).
- **Not yet in PR A (by design):** the `ThunderstoreSettings` gate ships with PR B (one-click
  protocol) since bare CLI verbs are inert; no UI, no protocol handler, no pilot game module.

### 17.3 Validation
- **Unit tests 37/37** (`tests/Networking/NexusMods.Networking.Thunderstore.Tests/`): parser
  (valid/invalid dependency strings, dashed namespaces, numeric-vs-lexicographic version
  compare), resolver (chain, diamond→highest wins, lower-version skip without API call, cycle
  termination, missing/unparsable deps reported, noDeps, missing root), DTO deserialization
  against captured live responses.
- **Full solution: 0 errors** (warnings unchanged).
- **Live end-to-end** (`as-main thunderstore …`): `resolve -p bbepis-BepInExPack` → 6-package
  closure, 0 errors; `download` → all 6 downloaded into the Library with correct names/sizes
  (BepInExPack 635KB … BepInEx_GUI 3.5MB); re-run → "already in the Library" ×6, 0 downloaded.
  Library entities + archive analysis confirmed via the standard AddDownload pipeline.

### 17.4 Next (per DESIGN-modsources.md §13)
PR B: `ror2mm://` one-click (URL parser + `Ror2mmIpcProtocolHandler` + generalized scheme
registration + `.desktop` MimeType + `ThunderstoreSettings` gate). PR C: `DownloadsService`
de-Nexusing + `ThunderstoreDataProvider` (Library UI) + redownloadable capability. PR D: Risk
of Rain 2 pilot module + BepInEx loader/plugin installers + missing-loader diagnostic.
