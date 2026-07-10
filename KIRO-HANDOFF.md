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
10. **Package as a real installable app** (Brian, 2026-07-08): clickable executable, not
    `dotnet run` from source. Upstream left a dormant **PupNet Deploy** pipeline
    (`src/NexusMods.App/app.pupnet.conf` + `.github/workflows/build-linux-pupnet.yaml`,
    builds AppImage/RPM/deb) — revive it under the fork's own identity. Natural targets:
    AppImage first (upstream's shipping format; sidesteps the §19.5 desktop-file wart),
    AUR package for the CachyOS/Arch daily driver, Flatpak later. Gated on the re-brand
    (step 2) for naming; our vendored-assets work (§10.1) already removed the
    build-time-download steps that would have complicated CI.

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

---

## 18. Session log — 2026-07-08 (cont.) — Thunderstore one-click (PR B)

> PR #6 (PR A plumbing) merged by Brian → `linux-fork` @ `4eede716a`. This session adds PR B on
> branch `feature/thunderstore-one-click`: `ror2mm://` one-click installs + the settings gate +
> generalized URI-scheme registration. Design: DESIGN-modsources.md §7.

### 18.1 What was built
- **`Ror2mmUrl.TryParseInstallUrl`** (Abstractions.Thunderstore) — parses
  `ror2mm://v1/install/{host}/{ns}/{name}/{version}[/]`; accepts `thunderstore.io` +
  `{sub}.thunderstore.io` (r2modman parity), rejects near-miss hosts (`notthunderstore.io`),
  unknown protocol versions/actions, malformed names/versions.
- **`ThunderstoreSettings`** (Abstractions.Thunderstore, registered via `AddSettings<>` in
  `AddThunderstore()`) — `EnableThunderstore`, default `ApplicationConstants.IsDebug` (ON in
  debug, OFF in release), Experimental section, RequiresRestart.
- **`IIpcProtocolHandler.IsEnabled`** — new default-true interface property; disabled handlers
  are skipped by scheme registration and no-op in `Handle`. Nxm handler untouched (default).
- **`Ror2mmIpcProtocolHandler`** (App.Cli, beside the nxm handler; App.Cli now refs
  Networking.Thunderstore) — settings-gated; parse → `IsAlreadyDownloaded` short-circuit
  (sends the same `CliMessages.ModDownloadFailed(AlreadyExists)` the nxm handler uses, so
  existing UI toasts work) → resolve closure (abort+message on incomplete) → download each
  missing package via `ILibraryService.AddDownload` → `ModDownloadSucceeded` for the clicked
  root package. TaskCanceled swallowed like nxm.
- **`UriSchemeRegistration`** (App.Cli, hosted service) — iterates all `IIpcProtocolHandler`s
  and registers each enabled handler's scheme with `IOSInterop`. **Retired
  `HandlerRegistration`** (NexusWebApi's hardcoded nxm registrar — file deleted, DI line
  removed). Adding a future scheme (modrinth://) is now: implement handler, add one DI line.
- `.desktop` MimeType now `x-scheme-handler/nxm;x-scheme-handler/ror2mm`; new CLI verb
  `associate-ror2mm` mirrors `associate-nxm`.

### 18.2 Validation
- Full solution 0 errors; Thunderstore tests **53/53** (16 new `Ror2mmUrlTests`).
- Live: startup now runs `xdg-settings set default-url-scheme-handler` for **both** nxm and
  ror2mm (debug build → gate ON); `xdg-mime query default x-scheme-handler/ror2mm` →
  `com.nexusmods.app.desktop`. `protocol-invoke -u "ror2mm://v1/install/thunderstore.io/
  bbepis/BepInExPack/5.4.2100/"` → resolved (1-package closure), downloaded into Library,
  "Completed ror2mm install"; re-invoke → "already been downloaded and will be skipped".
- NOT verified: the browser-click → running-GUI path (SingleProcess forwarding) and the UI
  toast for a ror2mm download — needs a manual GUI session (same class of follow-up as the
  recognition toast, §16.4).

### 18.3 Next
PR C: DownloadsService de-Nexusing + ThunderstoreDataProvider (Library UI) + redownloadable
capability + localized strings. PR D: RoR2 pilot game module + BepInEx installers.

---

## 19. Session log — 2026-07-08 (cont.) — Downloads/Library UI de-Nexusing (PR C)

> PR #7 merged → `linux-fork` @ `c94226c4e`. Branch `feature/thunderstore-ui` carries PR C:
> the one non-additive slice of the mod-source plan — the Downloads page and Library UI stop
> assuming Nexus. Design: DESIGN-modsources.md §9.1/§9.2, §13 PR C.

### 19.1 `ILibraryDownloadJob` — the source-agnostic downloads seam
New interface in `Abstractions.Downloads`: `DisplayName`, `GameId` (OUR game identity, not
Nexus's), `DownloadPageUri`, `MetadataEntityId` (for icons), `IJob? InnerJob` (wrapper jobs
expose the inner HTTP job that observables/pause/resume actually live on; jobs that ARE the
HTTP transfer return null), and `FindLibraryFile(IDb)` (source-specific completed-download →
library file lookup). `INexusModsDownloadJob` and `IThunderstoreDownloadJob` both extend it:
- `NexusModsDownloadJob` maps NexusModsGameId→GameId at Create() via `GetServices<IGameData>()`;
  `FindLibraryFile` = the FindByFileMetadata logic that used to live inside DownloadsService.
- `ThunderstoreDownloadJob`: GameId = default (packages are global; game unknown at download
  time → shows as unknown in the Game column until PR D), InnerJob = null, `FindLibraryFile`
  via the version-metadata LibraryItems backref.

### 19.2 DownloadsService + model + UI retype (GameId everywhere)
- `DownloadsService` subscribes `GetObservableChangeSet<ILibraryDownloadJob>()`; progress
  observables come from `InnerJob ?? change.Current`; the issue-#3892 pause/resume unwrap is
  now `InnerJob`-based; `ResolveLibraryFile` invokes a resolver captured per-download
  (`DownloadInfo.LibraryFileResolver`) so completed downloads resolve after the job leaves the
  monitor, for any source. All Nexus usings dropped from the service.
- `DownloadInfo.GameId`, `IDownloadsService.{GetDownloadsForGame,GetActiveDownloadsForGame,
  PauseAllForGame,ResumeAllForGame}`, `IDownloadsServiceExtensions`, `DownloadsFilter`,
  `DownloadsPageContext.GameScope`, `IDownloadsDataProvider.ResolveGameName` all retyped
  `NexusModsGameId`→`GameId`; `ResolveGameName` matches on `Game.GameId`; the game-scoped
  Downloads left-menu item passes `Game.GameId`. ⚠️ Old persisted workspace layouts with a
  game-scoped Downloads page deserialize to a non-matching GameId (header "Unknown Game",
  empty list) — reopen the page once to fix; accepted for a dev fork.
- `DownloadsDataProvider.CreateIconComponent` still loads `NexusModsFileMetadata` from
  `FileMetadataId`; for Thunderstore ids `IsValid()` is false → fallback icon (real
  Thunderstore icons = follow-up pipeline work).

### 19.3 Library page: `ThunderstoreDataProvider` + removal info
- `ThunderstoreDataProvider : ILibraryDataProvider, ILoadoutDataProvider` (App.UI, registered
  beside the other providers; App.UI now refs Abstractions.Thunderstore). Modeled on
  LocalFileDataProvider + the ItemVersion column from `ThunderstoreVersionMetadata.VersionNumber`.
  Fallback thumbnail; changelog/mod-page actions disabled (package-page link + real icons are
  follow-ups). `GetAllFiles` returns [] until Thunderstore items gain a game association (PR D).
- `LibraryItemRemovalInfo`: "redownloadable" now = Nexus OR Thunderstore item (delete-warning
  UX treats one-click packages as safely deletable).

### 19.4 Validation
- Full solution 0 errors. Suites: Library.Tests **3/3** (incl. NEW
  `DirectDownloadJobs_SurfaceAlongsideWrapperJobs` — a Thunderstore-shaped direct job and the
  wrapper-shaped Nexus job surface together, direct pause/resume routes to the job itself, id-
  tracked and self-contained against the shared-singleton cache), Thunderstore **53/53**,
  Steam 11/12 (pre-existing skip), Synchronizer **12/12**, DataModel **232/232**.
- Test-harness notes: `TestNexusModsDownloadJob`/`DownloadJobFactory` gained the new members
  (synthetic NexusModsGameId→GameId mapping); new `TestDirectDownloadJob` polls its completion
  source with periodic `YieldAsync` so cooperative pause/cancel work; existing exact-count
  waits are order-fragile against the singleton cache — new tests should track by job id.
- Live headless: `protocol-invoke ror2mm://…HookGenPatcher/1.2.3/` through the refactored
  pipeline → 2-package closure downloaded, completed cleanly.
- ⚠️ NEEDS GUI VERIFICATION (Brian): with the app open, click "Install with Mod Manager" on
  thunderstore.io → Downloads page should show the download live (name, size, progress,
  pause/resume) with game "Unknown"; Library page should list Thunderstore items with their
  version; deleting one should NOT warn "not redownloadable". This also closes the §18.2
  browser-click forwarding check.

### 19.5 GUI verification PASSED + desktop-file bug found & fixed (same day)
Brian ran the app and clicked "Install with Mod Manager" on a RoR2 mod: the running app
received the link and downloaded the 10-package closure (ArtilleristMod 1.2.2 + R2API family
+ BepInExPack 5.4.2109 — correctly stored beside the already-present 5.4.2121). Closes the
§18.2 forwarding check AND §19.4's GUI check. First attempt exposed a PRE-EXISTING Linux bug:
for framework-dependent launches (`dotnet NexusMods.App.dll`, i.e. running from source),
`Environment.ProcessPath` is the bare dotnet host, so the registered `.desktop` `Exec` lost
the assembly argument and nxm://+ror2mm:// links were silently dropped (upstream never saw it
— they shipped AppImages). Fixed in `LinuxInterop.GetRunningExecutablePath`: prefer the
apphost binary next to the entry assembly (`fa3c8b7bf` on the PR C branch).

### 19.6 Cross-source design principle (Brian) — recorded in DESIGN-modsources.md §15
Games often live on BOTH Nexus and Thunderstore with diverging mods/versions. Standing rules:
multi-source mods must coexist in one loadout (PR D: RoR2 keeps its NexusModsGameId beside
the Thunderstore community); per-game discovery CTAs should offer both storefronts;
cross-source mod identity stays (source, native-id) — no fuzzy merging until update checking
makes it worthwhile.

### 19.7 Next
PR D: RoR2 pilot game module + BepInEx loader/plugin installers + missing-loader diagnostic
(gives Thunderstore items a game association → Game column, GetAllFiles, install targeting).

---

## 20. Session log — 2026-07-08 (cont.) — Risk of Rain 2 + BepInEx installers (PR D)

> PR #8 merged → `linux-fork` @ `a4f24b66e`. Branch `feature/ror2-bepinex` carries PR D — the
> final Phase 1 slice: a real game module so Thunderstore downloads become installable.

### 20.1 The big discovery: RoR2 is not on Nexus Mods at all
No `riskofrain2` domain exists in games.json — RoR2 modding is Thunderstore-exclusive. The
module ships `NexusModsGameId = None`, making RoR2 the first game this app supports that
upstream never could — and forcing the §9.4 generalization (design doc updated):

### 20.2 Nexus-less-game support (the §9.4 fix, kept migration-free)
`GameInstallMetadata.GameId` keeps its persisted Nexus-id meaning; Nexus-less games write a
**zero sentinel** and are matched by **install Path + Store** (Path already indexed):
- `GameRegistry.TryGetMetadata` (Path+Store fallback branch) and `TryGetGameInstallation`
  (sentinel + path disambiguation); `LoadoutManager.ManageInstallation` (sentinel instead of
  `.Value` throw); `Loadout.ReadOnly.Game` (sentinel → resolve among Nexus-less games by
  display name — this was crashing every loadout operation in tests until fixed).
- Crash guards at every `.NexusModsGameId.Value` site a Nexus-less game reaches:
  `ManuallyAddedLocator` (was: startup crash with ANY Nexus-less game registered!),
  `MyGamesViewModel` collections count, `LibraryViewModel` Nexus CTAs (no-op; carries
  `TODO(design §15)` to offer Thunderstore instead), `FileHashesService.SuggestVersionData`.
- `LocalGameVersionRecognizer.CanRecognize` now declines Nexus-less games (version
  definitions are keyed on the Nexus id, so the Recognize button would show but never
  succeed; RoR2 is ~4GB < the 5GB backup fuse, so managing it backs up normally).

### 20.3 The game module — `src/NexusMods.Games.RiskOfRain2/`
- `RiskOfRain2Game : IGame, IGameData<RiskOfRain2Game>` — GameId "RiskOfRain2", Steam
  632360, `DefaultSynchronizer` (first module to use it — no game-specific sync rules),
  zero sort-order varieties, `LocationId.Game` only, primary file `Risk of Rain 2.exe`
  (Windows-only game, Proton on Linux — mirrors Cyberpunk's unconditional-path style).
  Placeholder art (ImageMagick-generated 144×144 + 600×900 webp; real art = follow-up).
- **`BepInExPackInstaller`** — detects a loader pack by the shallowest `winhttp.dll` with a
  `BepInEx/` folder beside it; deploys the pack-root contents to the game root; skips
  Thunderstore metadata; records `BepInExLoadoutItem` (version from Thunderstore metadata
  when the archive came from Thunderstore).
- **`BepInExPluginInstaller`** — r2modman routing: `plugins|patchers|monomod|core` categories
  get a per-package subfolder (`BepInEx/plugins/{Namespace-Name}/…` — canonical name from the
  ThunderstoreLibraryItem identity, archive-stem fallback), `config/` deploys shared (no
  subfolder), explicit `BepInEx/` prefixes normalized, single wrapping folders stripped,
  package metadata skipped; loose files land in the package's plugins folder. Records
  `BepInExPluginLoadoutItem`.
- **`MissingBepInExEmitter`** — warns (with a Thunderstore link) when a loadout has BepInEx
  plugins but no loader pack.
- NOT in `ExperimentalSettings.SupportedGames` — visible in debug builds via EnableAllGames
  only, per the design's release-gating.

### 20.4 Validation
- Full solution **0 errors**. New `NexusMods.Games.RiskOfRain2.Tests`: **5/5** (pack routing +
  metadata skip, loose-file plugins subfolder, category routing incl. shared config, wrapping-
  folder strip + explicit BepInEx prefix, installer cross-gating) on the synthetic-zip harness
  (`ALibraryArchiveInstallerTests` + `UniversalStubbedGameLocator`).
- Regression: DataModel **232/232**, Synchronizer **12/12**, Library **3/3**, Thunderstore
  **53/53**, Steam 11/12 (pre-existing skip).
- RoR2 confirmed installed on this box (second Steam library) — Steam locator will find it.
- ⚠️ NEEDS GUI VERIFICATION (Brian): My Games should show "Risk of Rain 2" (placeholder art)
  → Add/manage it (first-ever manage of a Nexus-less game — exercises the whole §20.2 path,
  ~4GB backup on first manage) → install BepInExPack + the ArtilleristMod closure from the
  Library → check the missing-BepInEx diagnostic fires if the pack is skipped → Apply →
  verify `winhttp.dll` + `BepInEx/plugins/...` in the game dir → set Steam launch options
  `WINEDLLOVERRIDES="winhttp=n,b" %command%` (Proton needs the override; documented in design
  §10 — automating this is Phase 2) → launch and see mods load.

### 20.4b GUI test round 1 findings (Brian, same evening) — three fixes pushed to the PR branch
1. **Play button crashed** — `LaunchButtonViewModel` requires a registered `IRunGameTool`
   (`.First()` on an empty set); the module hadn't registered one. Fixed:
   `RunRiskOfRain2Game : RunGameTool<RiskOfRain2Game>` (Steam installs launch via
   `steam://run/632360`, so Steam's launch options apply).
2. **The locator managed a GHOST install.** Steam's home library held a stale
   `appmanifest_632360.acf` with `StateFlags=36` ("update started", not fully installed) and
   a folder containing no game files; the real game was nowhere (Brian believed it was on
   another drive — Steam disagreed). The app managed the ghost, "cleaned" it instantly,
   deployed mods into it, and launch failed with "Missing game executable". Fixed in
   `SteamLocator`: skip manifests without `StateFlags.FullyInstalled` (+ log). Bonus fix:
   the locator built `LinuxCompatibilityDataProvider` but never assigned it to the
   `GameLocatorResult` — now assigned (needed for fix 3, and likely an upstream bug).
3. **Manual `WINEDLLOVERRIDES` launch options eliminated** (Brian: "as easy as can be").
   `RunRiskOfRain2Game.Execute` now ensures the Proton prefix's `user.reg` has
   `"winhttp"="native,builtin"` under `[Software\\Wine\\DllOverrides]` before every launch
   (r2modman's mechanism; one-time `.bak` backup; never blocks the launch on failure; no-ops
   when the prefix doesn't exist yet — Wine creates it on first run, override lands on the
   next). Generalizing this to the BepInEx game family is Phase 2.

Recovery on the box: ghost folder + stale manifest removed; RoR2 must be properly installed
via Steam, then re-managed in the app (old ghost metadata/loadout stays dormant in the DB —
harmless; Path+Store matching means it never reattaches to a different install path).

### 20.5 Follow-ups
Real game art; Thunderstore CTA on the Library page for Nexus-less games (design §15 rule 2);
`config/` conflict semantics between packages; Proton WINEDLLOVERRIDES automation (pairs with
§3.1 umu work); eventual clean rekey of GameInstallMetadata onto our GameId with migration.

### 20.6 END-TO-END VERIFIED (Brian, 2026-07-08 evening) — Phase 1 complete
After the fix rounds below, the full arc works on real hardware: thunderstore.io one-click →
latest-version dependency resolution → Library → BepInEx pack/plugin installers → Apply →
Play (steam://run with automatic user.reg winhttp override) → **modded characters visible
in-game**. Verified twice (two different character mods).

Fix rounds during GUI testing (all on the PR #9 branch):
- Ghost-install saga: stale not-fully-installed Steam manifest → locator filter; ghost
  loadout in DB crashed startup pipelines AND (fatally) a UI-thread reactive pipeline during
  downloads → orphan-tolerance in MyGames/Spine/window-restore/MyLoadouts + new
  **`loadouts delete-orphaned`** CLI recovery verb; ghost purged from Brian's DB.
- Play button: needs a registered IRunGameTool (added RunRiskOfRain2Game).
- WINEDLLOVERRIDES automated via user.reg patch at launch (r2modman mechanism).
- **Dependency pins are FLOORS**: resolver now installs latest version of every dependency
  (root stays exact) — honoring pins literally installed pre-SotS R2API referencing
  Newtonsoft.Json 12 which the game no longer ships; every plugin failed. r2modman parity.

### 20.7 Brian's design backlog from the first real session (roadmap items)
1. **De-Nexus the app's own branding/icons** (ties into fork re-branding, roadmap step 2).
2. **Real artwork everywhere**: pull game tile/icon art for Thunderstore-only games (RoR2
   currently placeholder) AND mod thumbnails — Thunderstore package icons (IconUri is
   already stored on ThunderstorePackageMetadata; needs a resource pipeline like Nexus's),
   plus Brian reports missing thumbnail previews for Nexus mods in some views too.
3. **Library page: visual "Installed" indicator** — the installed-state column should be
   highlighted/badged, not plain text.
4. **Multi-source accounts**: Thunderstore login support (note: its read/download API is
   anonymous; login via OpenID exists mainly for ratings/submissions — value is smaller than
   Nexus's premium-download case but fits the peer-source identity), presented as a
   **source-account dropdown** replacing the single Nexus login button (Nexus / Thunderstore
   / future Modrinth-Minecraft) showing per-source login state. Long-term §7 work.
Also queued from this session: "not a clean install" dialog should not appear for games with
zero known-vanilla data (and clean-folder is dangerous there); generalize local recognition
to Nexus-less games (removes the External-Changes wart for RoR2); Library UX for multiple
downloaded versions of one package (r2modman shows one row per package).

---

## 21. Session log — 2026-07-08 (cont., Claude Code / Fable 5) — Phase 2 begins: the generic BepInEx game family (PR E)

> Directive §7 Phase 2. Design-doc-first: `DESIGN-bepinex-family.md` (repo root) is the
> reviewed design; Brian approved its four §14 decisions this session (**ALL ~200 BepInEx+Steam
> games at once** — not a curated subset; **vendor-only** schema with a re-vendor script;
> **runtime art fetch+cache** as its own later PR; **Valheim in** with force-Proton guidance).
> Work on branch `feature/bepinex-family` (PR E of E/F/G/H').

### 21.1 Research that shaped the design (two parallel agents)
- **Codebase:** the RoR2 module was already a generic BepInEx engine — only 6 constants + 2
  string literals were game-specific. `IGameData<TSelf>`'s static-abstract members are consumed
  NOWHERE generically (convention, not requirement) → a data-driven class can implement plain
  `IGame`. The only DI edge: `AddGame<T>`/`AddAllSingleton` = one singleton per TYPE → the
  family bypasses it with per-row factory registrations. All store locators (Steam/EGS/GOG/
  Xbox) build FrozenDictionaries that THROW on duplicate ids → parser dedups app ids, family
  carries Steam ids only (PR E), RoR2 excluded until PR G.
- **Ecosystem schema** (`thunderstore.io/api/experimental/schema/dev/latest/`, 0.3.0, 1.35 MB,
  296 games / 268 instances): per-instance it has everything an IGame needs (displayName,
  steam ids, exeNames, steamFolderName, dataFolderName, installRules, iconUrl, unique
  `settingsIdentifier`). **204/214 bepinex instances have byte-identical installRules** — the
  canonical 5 our installer already implements; 10 deviants are additive routes or
  `state`-tracking swaps (subnautica QMods). `state` tracking is trivial for us: loadouts
  already track per-file ownership (r2modman needs state files because it has none). No
  stable/versioned publication exists → vendor + validate, r2modman does the same. Schema has
  ZERO Linux/Proton knowledge; native-Linux builds detectable via `.x86_64` exeNames (15 games).
  Community slug must be derived from `packageIndex` (46 legacy games have no community block).

### 21.2 PR E implemented — `src/NexusMods.Games.BepInEx/`
- **Assets:** vendored `ecosystem-schema.json` (1.35 MB, embedded) + generated
  `bepinex-nexus-ids.json` (114/211 games name-matched to Nexus ids via
  `scripts/python/generate_bepinex_nexus_ids.py`, exact-unique normalized-name joins, 0
  ambiguous, verification set hand-asserted: subnautica 1155, valheim 3667, lethalcompany
  5848, gtfo 3657, ultrakill 3515, timberborn 4074, contentwarning 6301, repo 7398, peak 7867,
  subnauticabelowzero 2706; H3VR + RoR2 correctly Nexus-less).
- **`EcosystemSchemaParser`** → `BepInExGameData` rows: bepinex+game+visible+steam-id filter,
  duplicate-app-id dedup (first wins, ordered by settingsIdentifier), unsupported-trackingMethod
  rows dropped (fail the row, not the app), Nexus-less display-name dedup (zero-sentinel
  resolution needs unique names), Windows exe preferred as primary file, community slug from
  packageIndex. **211 candidates → ~200 rows registered.**
- **`GenericBepInExGame : IGame`** (instance-only, deliberately NOT `IGameData<TSelf>`) +
  `Services.AddBepInExGames()`: per-row `GameHolder` (lock + memoize) makes IGame/IGameData/
  ITool factories resolve ONE shared instance per row — the identity contract AddAllSingleton
  provides for class-per-game modules. GameId = `GameId.From(settingsIdentifier)`.
- **`ABepInExRunGameTool<T>`** — the RoR2 user.reg winhttp-override tool generalized as a
  shared base (no-ops without a Proton prefix); `RunBepInExGameTool` per family game,
  `RunRiskOfRain2Game` now a 3-line subclass.
- **Installers/emitter moved** from the RoR2 project verbatim (`NexusMods.Games.BepInEx.
  Installers`); `MissingBepInExEmitter` parameterized by community slug (links
  `thunderstore.io/c/{slug}/`), constructed per game. RoR2 project now references the family
  and is down to game class + tool + thin Services.
- **⚠️ MODEL FINDING (design §9 corrected):** MnemonicDB's query engine keys attributes by
  `{final-namespace-segment}/{attrName}` — the planned fresh `NexusMods.BepInEx.*` namespaces
  COLLIDED with the legacy `NexusMods.RiskOfRain2.*` shims at startup (duplicate key
  `BepInExLoadoutItem/Version`). Resolution: ONE model set keeping the historical
  `NexusMods.RiskOfRain2.*` attribute strings verbatim (commented at the definition site).
  Zero migration, no union queries; PR G needs nothing for markers. Lesson: model class
  final-segment names must be unique app-wide regardless of namespace.

### 21.3 Validation
- New `NexusMods.Games.BepInEx.Tests`: **11/11** (bundled-load invariants: >150 games, unique
  GameIds/app ids/Nexus-less display names, verification-set rows exact; synthetic-schema
  filter/dedup/mapping/exe-preference unit tests; TryGetCommunitySlug; DI registration:
  distinct instance per row, IGame↔IGameData reference-identical, stable resolution, shared
  installer singletons).
- Regression: RoR2 installer tests **5/5** (now exercising the family installers through
  RiskOfRain2Game), DataModel **232/232**, Synchronizer **12/12**, Library **3/3**,
  Thunderstore **54/54**, Steam 11/12 (pre-existing skip). Full solution: **0 errors**.
- **Live headless:** `as-main loadouts list` boots the app with ~200 family games registered —
  clean startup, Brian's 3 loadouts listed, clean shutdown (~0.6s). Then the money shot:
  `as-main steam recognize-game -a 264710` — **Subnautica (a schema-driven family game, never
  hand-written) located via the family registration and fully recognized: 15,760/15,760 files
  verified in ~4s**, version definition recorded against its Nexus id (1155). The §20.2
  dual-source path works for family games end-to-end.
- ⚠️ NEEDS GUI VERIFICATION (Brian): My Games in a debug build will now show ~200 games
  (EnableAllGames) — all with placeholder art until PR H'. Check: Subnautica appears and can
  be managed; RoR2 unchanged; a canonical-rules game one-click installs end-to-end.

### 21.4 Next
PR F: schema-driven installRules engine in `BepInExPluginInstaller` (Subnautica's QMods/state
rules become correct) + Subnautica pilot end-to-end. PR G: RoR2 folds into the family (delete
project, enable table row). PR H': runtime art fetch+cache (gcdn covers; groundwork for §20.7
mod icons). Later: native-Linux doorstop slice; runtime schema refresh.

---

## 22. Session log — 2026-07-08 (cont.) — PR F rule engine + a CRITICAL data-loss bug found and fixed

> Two intertwined deliverables: the Phase 2 install-rules engine (PR F), whose live pilot
> uncovered — and then validated the fix for — the worst bug in the fork's history.

### 22.1 ⚠️ CRITICAL BUG (pre-existing since the §12 recognition work): applying an
### overlay-recognized loadout DELETED THE ENTIRE GAME INSTALL
- **Trigger:** the PR F pilot — `loadouts manage -g Subnautica` (new verb) → install two mods
  → `loadout synchronize` → all 15,556 vanilla files deleted; only mods + saves survived.
- **Root cause:** the synchronizer's desired-state game layer came from SQL
  (`synchronizer.WinningFiles` → `file_hashes.loadout_files` → `MDB_*(DBName=>"hashes")`).
  The MnemonicDB query engine resolves a DBName to the FIRST connection registered under it —
  and the local-recognition overlay ALSO registered as "hashes", so it was silently shadowed.
  Version resolution/backup decisions use the C# FileHashesService union (which DOES see the
  overlay) — so the app said "version known, files restorable, no backup needed" while the
  sync said "zero game files wanted": every vanilla file → delete-without-backup. Armed since
  PRs #2–#5 for every overlay-recognized game (= every game newer than the frozen shipped DB);
  never triggered because no overlay-recognized loadout had ever been APPLIED.
- **Fix (PR #11, three layers):** (1) `BuildSyncTree` now sources game files from the C#
  hash-service API — one read path, one truth; SQL game layer ignored; a winning Deleted item
  suppresses the game file at its path. (2) Overlay registers as `hashes_overlay` and ALL
  FileHashesQueries.sql macros resolve per-database then union (entity ids are DB-scoped —
  joins must never cross DBs); stub registers an empty overlay stand-in. (3)
  `GuardAgainstVanishedGameFiles`: known version + zero game files in tree → refuse loudly.
- **The test harness reproduced the bug by construction:** the stub locator id
  ("StubbedGameState.zip") can't resolve through SQL either, so every synchronizer snapshot's
  synced state was MISSING the vanilla game files. Post-fix they appear as `{Game, …}` rows —
  6 Verify snapshots updated; that diff is the bug made visible.
- **Recovery:** Subnautica restored via `steam steam://validate/264710` (15,964 files,
  StateFlags 4); Steam left the deployed mod files untouched. The wounded loadout then
  synchronized cleanly on the fixed build: 0 extracted, 0 deleted, converged.
- **Lesson recorded:** never introduce a second read path for data the synchronizer acts on;
  registered-name lookups (query engine, DI) fail silently when names collide — grep for
  every consumer of a name before reusing it.

### 22.2 PR F — schema-driven installRules engine (branch `feature/bepinex-rules`)
- **`InstallRuleRouter`** (pure, precomputed, per-game): route-prefix match (full route or
  bare final segment, longest wins) → extension routing (longest ext wins, `.mm.dll` beats
  `.dll`) → default-location fallback. Placement by trackingMethod: `subdir`/
  `subdir-no-flatten` → `route/{Namespace-Name}/…`; **`state`/`none` → loose into the route**
  (r2modman needs state files because it lacks ownership tracking — our loadouts have it
  natively, so `state` collapses to loose deploy by construction). Explicit `BepInEx/` prefix
  that matches no route is normalized away (Phase 1 parity). Canonical-5 rules are the
  fallback for games without rules.
- `BepInExPluginInstaller` is routed by the router; `GenericBepInExGame` constructs a
  per-game instance with its rules + `relativeFileExclusions`; the parameterless DI singleton
  keeps canonical behavior for RoR2. `IsRouteSegment` keeps meaningful top folders (QMods/)
  safe from wrapper-stripping.
- **`loadouts manage -g <gameId>`** — new CLI verb, the headless Add-game button.
- Tests: **48/48 family** (22 router tests against REAL schema rule sets: canonical parity,
  Subnautica state/QMods, GTFO GameData+Assets, ULTRAKILL "UMM Mods", H3VR .hotmod/.h3mod
  extension routes, Valheim SlimVML, all-201-games compile sweep; 4 harness integration tests
  through a real schema-driven Subnautica `GenericBepInExGame`) + RoR2 5/5 unchanged.

### 22.3 Subnautica pilot — END-TO-END on the fixed build (headless, real install)
`loadouts manage -g Subnautica` (21s — recognition made the first-manage backup near-zero;
this was a 7GB copy pre-recognition) → `loadout install` of real Thunderstore packages
(BepInExPack_Subnautica 5.4.1901 + Tobey-SnapBuilder 1.4.1, a genuine QMods-layout package)
→ apply → verified on disk: pack hoisted to game root (winhttp.dll, doorstop_config.ini,
BepInEx/core, BepInEx/config), **SnapBuilder routed by the deviant rules to
`QMods/SnapBuilder/{mod.json,dll}` loose** (metadata + relativeFileExclusions skipped) →
group items deleted + re-apply → **clean vanilla restored by the app** (15,939 files, all
mod files gone, game intact). The first schema-driven game to complete the full
manage→mod→apply→unmod lifecycle.

### 22.4 Findings / follow-ups
- **`loadout revert` CLI verb does not actually restore the target revision** (created new
  revisions but ModCount stayed 2; cleanup was done via `loadout group items delete` + sync).
  Investigate UndoService.RevertTo vs the verb's revision indexing.
- Bannerlord's loadout was armed with the same bug — safe now, but it must only be applied on
  builds containing PR #11.
- The §22.1 lesson candidates for upstreaming to MnemonicDB: duplicate-name registration on
  the query engine should throw instead of shadowing.

---

## 23. Session log — 2026-07-08 (cont.) — THE FORK IS NAMED: **APOCRYPHA**

### 23.1 The name (Brian's pick, collision-checked)
**Apocrypha** — the writings excluded from the official canon; i.e. exactly what mods are.
Tagline candidate: *"Canon is just the beginning."* Identity primitives: display name
"Apocrypha", binary/CLI `apocrypha`, AUR `apocrypha` (claim at packaging time, roadmap
step 10), reverse-DNS app id direction `io.github.jeagermeister.apocrypha`.
- Collision sweep (two rounds, web-verified): **no tool/launcher/mod manager owns the word**;
  bare AUR + Flathub free; unregistrable common noun (ZeniMax's aggression targeted the
  product title "Scrolls", never location names — Skyrim's Apocrypha realm is flavor synergy
  for this exact audience, not a conflict).
- REJECTED with cause: **Grimoire** (FATAL — grimoiremods.com, an active open-source Linux
  mod manager, AUR `grimoire-bin`), **Crucible** (FATAL — AUR `crucible` is a Linux game
  launcher; Destiny owns the word), **Strata** (Strata Source engine — in-community
  collision), **Runebook** (runebook.gg game + runebook.ai startup + eternal "runbook"
  confusion), **Bifrost** (saturated). Runners-up: **Outpost** (clean, generic),
  **Bindery** (cleanest namespace, zero gaming soul).

### 23.2 Rebrand scope & inventory (full sweep done; ~29 files, ~45-55 edits)
Scope rule: internal `NexusMods.*` C# namespaces STAY (Phase 1 §14 decision). Nexus remains
a first-class mod SOURCE (login, nxm, API untouched) — the rebrand renames the ship, not the
harbor. Key findings:
- **Display strings:** MainWindow `Title="Nexus Mods App"`, Welcome overlay "Nexus Mods App
  Preview", 2 Language.resx keys (`MetricsOptIn_MainMessage`, `Updated_ViewUninstallDocs`)
  duplicated across 8 locale resx files, Windows registry `ApplicationName`.
- **OS identity (migration-sensitive):** `com.nexusmods.app` AppId spans the .desktop file,
  `LinuxInterop.Protocol.cs:11` (writes + xdg-registers it at runtime, incl. nxm/ror2mm
  handlers), AppStream metainfo `<id>`, pupnet `AppId`, `StartupWMClass`, Windows ProgIDs +
  `SOFTWARE\Nexus Mods\...` registry path, single-process sync file name.
- **Data dirs (THE dangerous one):** the literal base name `NexusMods.App`/`NexusMods_App`
  is derived independently in SIX places (DataModelSettings:109 data dir, LoggingSettings:135,
  JsonStorageBackend:45 configs, IFileExtractorSettings:45 temp, AppDirectoryAuthStorage:10
  Steam auth, CliSettings:36-43 sync) — no shared constant. Renaming strands loadouts/
  library/DB without a one-time move-and-migrate step.
- **Art:** icon.ico/svg (pupnet + csproj), nexus-logo*.{ico,svg} incl. the login-overlay
  Nexus wordmark, repo Nexus-Icon.png. Brian is producing Apocrypha art.
- **Phone-home:** upstream **Mixpanel tokens + endpoint** (EventTracker.Request.cs:18,
  JsonText.cs:14), User-Agent `NexusModsApp` (ApplicationConstants:180), update checker +
  in-app changelog fetch upstream Nexus-Mods/NexusMods.App, ConstantLinks (Discord/forums/
  status/privacy), collection-upload string "Created with the Nexus Mods app" (published
  publicly on nexusmods.com), metainfo "successor to Vortex" line.

### 23.3 Rebrand PR slicing (after PRs #11/#12 merge)
1. **PR R1 — strings + assets** (cosmetic, revertible): titles, overlay, resx ×9, .desktop
   Name, metainfo name/summary, pupnet friendly/publisher names, collection "Created with
   Apocrypha", interim generated icon until Brian's art lands.
2. **PR R2 — links, telemetry, update path** (behavioral, no data risk): ConstantLinks →
   fork, changelog/updater → Jeagermeister repo, User-Agent → "Apocrypha", Mixpanel
   disposition, docs deep-links.
3. **PR R3 — identity + data migration** (the risky one, gated): AppId
   `io.github.jeagermeister.apocrypha` everywhere + old .desktop/registry de-registration +
   the six data-dir constants unified behind ONE shared constant + one-time migration that
   moves `~/.local/share|state/NexusMods.App` → `Apocrypha` (and Windows/macOS equivalents).
4. **PR R4 — packaging/CI:** pupnet AppBaseName/PackageName, workflows (drop the
   release-to-nexusmods job), releases-to-appstream.py OWNER/REPO, NuGet.Build.props;
   GitHub repo rename `Jeagermeister/NexusMods.App` → `Jeagermeister/Apocrypha` (GitHub
   auto-redirects) timed with this PR. Feeds roadmap step 10 (PupNet AppImage under the
   Apocrypha identity).

### 23.4 Rebrand decisions (Brian, 2026-07-08)
1. **Telemetry: ripped out entirely** — remove the Mixpanel endpoint/tokens/phone-home, not
   just stubbed. Privacy-first identity; opt-in analytics can be revisited someday.
2. **Links: fork-owned everything** — Help menu/updater/changelog → the Jeagermeister repo;
   upstream Discord/forums/statuspage links dropped until Apocrypha has its own; privacy
   policy link leaves with the telemetry.
3. **Timing: rebrand starts after PRs #11 and #12 merge** (merge order: #11 critical sync
   fix FIRST, then #12 rules engine). PR R1 then cuts clean from linux-fork.

### 23.5 Rebrand progress (2026-07-08/09)
- **PR #13 (R1, MERGED):** display strings ×9 locales, .desktop Name, metainfo fork story,
  pupnet display fields, "Created with Apocrypha" collection stamp, Brian's icon set wired
  (kit at `~/Source/VortexApp_Artwork/apocrypha_app_icon_set/` — freedesktop/AppDir/Flathub
  ladders staged for R4). Kept as SOURCE branding: nexus-logo.svg (IconValues.NexusColor) +
  login-overlay Nexus wordmark.
- **Repo RENAMED: `Jeagermeister/Apocrypha`** (GitHub auto-redirects the old name; local
  remote updated).
- **PR #14 (R2, open):** telemetry GONE not just off — upstream had THREE phone-home
  surfaces all gated on EnableTracking (Mixpanel EventTracker w/ live tokens, Matomo
  TrackingDataSender, OTel exporters); none register anymore regardless of settings, the
  Mixpanel endpoint/loop/tokens deleted, Welcome-overlay opt-in expander + Settings toggle
  removed (property kept for JSON compat). Help/Welcome keep ONE GitHub link → fork;
  Discord/forums/status/privacy dropped. Updater + in-app changelog fetch from the fork
  (upstream's releases would have been offered as "updates"!). User-Agent → "Apocrypha".
  Full fork README (identity, BepInEx family, build instructions, GPL provenance +
  non-affiliation).
- Remaining: **R3** (AppId io.github.jeagermeister.apocrypha + StartupWMClass + Windows
  registry + the SIX data-dir constants unified + one-time move-migration + old-registration
  cleanup) and **R4** (pupnet AppBaseName/PackageName, workflows incl. dropping the
  release-to-nexusmods job, releases-to-appstream.py, NuGet.Build.props, icon ladders into
  packaging → feeds roadmap step 10 AppImage).

---

## 25. Session log — 2026-07-09 (Claude Code / Fable 5) — PR H': runtime game art (fetch + cache)

> Brian picked PR H' from the §24 menu ("start with this one as it should be quick; then
> each issue, each improvement, one by one"). Branch `feature/bepinex-art`, **PR #16** into
> linux-fork. Design §10 / decision §14.3 as written: a caching stream factory at the game
> layer — zero UI changes, all four `IStreamFactory` consumers (GameWidget tile,
> LoadoutCard, Spine, ImageHelper) light up at once.

### 25.1 What landed
- **`NexusMods.Sdk/IO/CachedHttpStreamFactory`** (reusable — the natural carrier for the
  §20.7 Thunderstore *mod* icons later): first read downloads → temp file → atomic
  `MoveToAsync` publish; later reads serve the disk cache; a semaphore dedups concurrent
  downloads; ANY failure (offline/404/timeout/mid-body abort) serves the fallback factory,
  leaves NO cache entry, and retries on the next read. 30s internal timeout (the shared
  HttpClient's 100s default + 3× Polly retries is UI-hostile for best-effort art).
- **Schema/parser:** the vendored schema's `communities` section is now modeled.
  `BepInExGameData.IconUrl` renamed → `CoverUrl` (the instance's 360×480 cover; every row
  has one) + new `CommunityIconUrl` (the community's 192×192 icon; **162** of the rows'
  communities have one — subnautica/valheim are legacy communities without a block).
- **`GenericBepInExGame`:** `TileImage` ← cover, `IconImage` ← community icon; embedded
  placeholders remain the fallback AND the whole story in lean containers (art wires via
  `GetService` — headless verbs / DI tests without HttpClient+IFileSystem keep
  placeholders). Cache: `~/.local/share/NexusMods.App/Cache/GameArt/{asset-name}` via
  `GameArtCache` — carries a `TODO(linux-fork)` marker: one MORE base-dir-name derivation
  for R3's §23.2 inventory (pure cache; the R3 move-migration may skip it).

### 25.2 Verification
- **Tests** (BepInEx.Tests 51/51, Sdk.Tests 42/42): +6 factory tests (TUnit,
  InMemoryFileSystem + counting handler — caches once, serves without HTTP, pre-seeded
  cache, 404→fallback→retry-heals, 8-way concurrency = 1 request, mid-body abort leaves no
  debris); parser art-resolution pins (synthetic + bundled: lethal-company both URLs,
  subnautica icon-less); registration wiring (full container → factories with the right
  gcdn URIs, lean container → placeholders).
- **Live on the box:** one launch with a cleared cache → **162 assets atomically cached**
  (160 icons + covers for the games installed here: subnautica, cities-skylines-ii), all
  valid webp, 0 leftover `.tmp`, 0 fallback log lines, 1.1 MB total.

### 25.3 Found while verifying (pre-existing, NOT this PR)
- **`linux-fork` full-solution build was RED:** `tests/NexusMods.Backend.Tests/
  EventTrackerTests.cs:52` still called `EventTracker.PrepareRequest()`, which R2 (#14)
  deleted with the Mixpanel loop. **Fixed in PR #17** (same day, after #16 merged): the
  orphaned test + its Verify snapshot removed — the whole file tested only the deleted
  request builder, and the tracker never runs anyway (unregistered, no-op ExecuteAsync).
  Backend.Tests 59/59; **the full 96-project solution builds 0-error again**.

---

## 27. Session log — 2026-07-09 (cont., Claude Code / Fable 5) — Rebrand R3: one identity + the data migration

> The careful slice (§23.3 item 3), done and **verified live on the dev box** (33GB moved,
> loadouts intact). Branch `rebrand/r3-identity-migration`, **PR #18** into linux-fork.

### 27.1 What landed
- **`NexusMods.Sdk/ApplicationIdentity`** — THE definition: `DataDirectoryName="Apocrypha"`
  (no dot → the macOS `NexusMods_App` special-case dies everywhere) + `AppId=
  io.github.jeagermeister.apocrypha` + the legacy names for migration/cleanup. **EIGHT**
  previously independent derivations unified (§23.2 knew six; recon found two more:
  `FileHashesServiceSettings:75` and `JsonStorageBackend:45`): DataModelSettings,
  LoggingSettings, IFileExtractorSettings, JsonStorageBackend, FileHashesServiceSettings,
  AppDirectoryAuthStorage, CliSettings, GameArtCache.
- **`NexusMods.Sdk/LegacyDataMigration`**, hooked at the very top of `Program.Main` (before
  `BuildSettingsHost()` — the earliest point; the settings host otherwise creates Configs
  first). Per-base atomic `Directory.Move` (Linux: XDG_DATA_HOME + XDG_STATE_HOME +
  LocalApplicationData; Windows/macOS equivalents), THEN rewrites legacy fragments inside
  `Configs/*.json`. **CRITICAL recon finding that shaped this:** the persisted settings
  JSON pins the OLD paths (`MnemonicDBPath.File="NexusMods.App/DataModel/..."`, log paths,
  the absolute CLI sync-file path) and `SettingsManager.Get` prefers persisted over
  defaults — a naive rename+constant-change would have booted a FRESH EMPTY datom store
  and stranded the 32GB. Idempotent (move gated old-exists/new-missing; rewrite re-scans
  every boot) + crash-safe (rename atomic; crash between move and rewrite self-heals).
- **Linux id:** desktop/metainfo/releases files renamed to the new id (git mv), Backend's
  embedded-resource reference + App csproj + pupnet `AppId`/`DesktopFile`/`MetaFile`
  follow; `LinuxInterop` registers the new id and **deletes the pre-rebrand desktop files**
  on registration. `StartupWMClass`/`X-AppImage-Name` deliberately stay `NexusMods.App`
  until R4 renames the binary — **verified live: that IS the WM_CLASS the app presents**
  (xprop on the running window).
- **Windows id (code-only, needs R4 QA):** ProgIDs `Apocrypha.{scheme}`,
  `HKCU\SOFTWARE\Apocrypha\Capabilities`, RegisteredApplications value `Apocrypha`,
  best-effort legacy-key removal (`SOFTWARE\Nexus Mods` tree, old ProgIDs, old value).
- Log basenames → `apocrypha.main/slim.*` (issue-triage CI script accepts old AND new);
  docs' data-path references updated; OTel meter/service name → Apocrypha (its "don't
  change" Mixpanel pin died with R2).

### 27.2 Verification
- **Tests:** 11-case migration suite (Sdk.Tests 53/53) on a REAL-filesystem sandbox —
  `CreateOverlayFileSystem` maps the KnownPath bases into a temp dir, so the atomic move
  runs for real on every CI OS. Cases: move+rewrite, no-op, idempotency, both-exist guard,
  crash-between-move-and-rewrite resume, fragment fixtures taken verbatim from the live
  box (incl. negatives: DeviceId GUID, Nexus-Mods/game-hashes URL untouched). Full
  solution 0 errors; DataModel 232, Backend 59, BepInEx 51, Synchronizer 12 all green.
- **Live migration on the box:** app start moved `~/.local/share/NexusMods.App` (33GB) +
  `~/.local/state/NexusMods.App` instantly, rewrote 4 config files, opened the REAL datom
  store ("existing state found, last tx 1a5"), logs to `Apocrypha/Logs/apocrypha.main.
  current.log`, the 162-file art cache rode along, xdg handlers (nxm+ror2mm) re-registered
  to the new desktop id and the old `com.nexusmods.app.desktop` was removed. Zero
  `NexusMods.App` dirs remain.

### 27.3 Gotcha worth remembering (cost one recovery loop)
The FIRST live run hit the both-exist guard: **running the test suites had pre-created a
fresh empty `~/.local/share/Apocrypha`** — `JsonStorageBackend`'s constructor eagerly
`CreateDirectory()`s against `FileSystem.Shared`, and Backend.Tests instantiate it for
real (pre-existing hygiene issue, invisible before because the dir always existed). The
guarded migration correctly refused, the app booted a fresh empty store; recovery = kill
app, park the fresh dirs as `~/.local/{share,state}/Apocrypha.fresh-discard` (safe to
delete), re-run → clean migration. Follow-up queued: Backend.Tests should overlay-map
their filesystem. Also killed: a stray app instance from earlier WM_CLASS probing that
had silently survived a `kill` (verify process death, not just send the signal).

---

## 28. Backlogs & standing queue (pointer lives at §30)

### 28.1 Brian's UI backlog (added 2026-07-09 post-R3 — de-Nexus the app's face)

1. ~~**Spine "Home" button → Apocrypha icon**~~ **DONE — §29 (PR #20).**
2. **Search box in "Other supported games"** (small-medium). `App.UI/Pages/MyGames/
   MyGamesView.axaml:99` — the section lists every supported-but-undetected game, ~200
   rows since PR E. Add a filter TextBox above the wrap panel + a case-insensitive
   DisplayName filter in `MyGamesViewModel` (games flow through DynamicData → `.Filter()`
   on an observable of the search text). Consider the same box filtering the Detected
   section too.
3. ~~**Orange → PURPLE accent**~~ **DONE — §29 (PR #20).**
4. **Login (research note — nothing to build).** "Login" is the NEXUS MODS account login,
   not an Apocrypha account: OAuth2+PKCE against `users.nexusmods.com/oauth`, public
   client id `nma` (`Networking.NexusWebApi/Auth/OAuth.cs:20-21`), browser round-trip,
   redirect `nxm://oauth/callback` caught by our protocol handler. It authorizes API/CDN
   calls as that user (downloads, collections, premium). No Apocrypha website or account
   system is needed for any of this; Thunderstore's read API needs no login at all.
   Apocrypha-own accounts would only matter for a future sync/sharing service — nothing
   blocks on it. Related: §20.7 multi-source login dropdown.

Standing follow-up queue: **OAuth session-expiry UX** (Brian hit "Invalid new token in
OAuth2MessageFactory" at every launch: an expired Nexus session whose refresh fails —
`OAuth.RefreshToken` (`Auth/OAuth.cs:89-91`) never checks the HTTP status and blindly
deserializes Nexus's error body → null-field token → cryptic log, user never told they're
logged out; fix = check status, log the OAuth error body, surface a "Nexus session
expired — log in again" toast; remedy meanwhile: re-login in-app), **MyGames tile
misalignment** (Brian, 2026-07-09: one game's icon+tile sits slightly HIGHER than its row
neighbors — suspects: a two-line game name or the `IsFound` extra info row making one
`MiniGameWidget` taller inside the FlexPanel row (`MiniGameWidget.axaml` rows 42-70,
`MyGamesView.axaml` FlexPanel `Wrap`); fix = pin a uniform widget height or set FlexPanel
`AlignItems`), Backend.Tests real-FS hygiene (§27.3), Brian deletes the
`Apocrypha.fresh-discard` dirs, §20.7 backlog (Installed badge, clean-install dialog,
Nexus-less recognition, multi-version Library UX; mod icons can ride
`CachedHttpStreamFactory`), `loadout revert` verb doesn't restore (§22.4), localization of
new strings, GUI check of the recognition toast.

---

## 29. Session log — 2026-07-09 (cont.) — UI: de-Nexus the face (§28.1 items 1+3)

> Branch `ui/de-nexus-face`, **PR #20**. Two backlog items in one visual PR.

- **Orange → PURPLE:** `BrandColors.axaml` — the 11 `BrandPrimary50–950` StaticResource
  lines remapped `PrimitiveOrange*` → `PrimitiveViolet*`; the semantic layer follows
  untouched. Verified NOTHING else consumes `PrimitiveOrange` directly. Hardcoded-hex
  sweep: 11 pictogram SVGs (`App.UI/Assets/Pictograms/`) carried raw orange ramp values —
  recolored to the violet counterparts (#FB923C→#A78BFA, #F97316→#8B5CF6, #FDBA74→#C4B5FD,
  #EA580C→#7C3AED). KEPT orange: `nexus-logo.svg` (#D98F40 — Nexus's own mark as a
  SOURCE).
- **Spine Home button → the tome:** `apocrypha.svg` from the icon kit copied to
  `App.UI/Assets/apocrypha-logo.svg` (the `Assets\**` AvaloniaResource wildcard picks it
  up), new `IconValues.Apocrypha` (AvaloniaSvg, next to the source-branding Nexus marks
  with a comment drawing the line), and the ONE setter in `SpineButtonStyles.axaml`
  repointed `IconValues.Nexus` → `IconValues.Apocrypha`.
- Build 0 errors; app launched clean for Brian's visual pass (purple accent + tome Home).
- Process note: PR #19 turned out to be UNMERGED despite the merge being announced —
  caught by `gh pr view` (the remote tip was still #18). Lesson: verify merge state on
  the remote, not from conversation. This branch fast-forwarded over
  `origin/docs/ui-backlog` so it carries the §28.1 docs either way.

### 29.1 Search box in "Other supported games" (§28.1 item 2 — PR #21)

`feature/games-search`. The section's collection was a one-shot static list; now a stable
`ObservableCollection` backing (`SupportedGames` wraps it once — also fixes the fragile
rebind-on-reassignment) refilled by a `WhenAnyValue(SupportedGamesSearchText)`
subscription: trim → `DistinctUntilChanged` → case-insensitive DisplayName `Contains`.
The coming-soon widget stays appended ALWAYS — it doubles as the no-results outcome and
keeps the request-a-game path visible. View: plain `TextBox` + `IconValues.Search`
`InnerLeftContent` under the section heading (hardcoded English watermark, matching the
hardcoded section title — both on the §20.7 localization sweep), two-way bound in
code-behind. With this, **all four §28.1 asks are closed** (item 4 was research-only).

### 29.2 The layout design session (Brian live, 2026-07-09) → `DESIGN-app-layout.md`

Brian raised two directions: add Minecraft via his Prism Launcher clone, and a design
session on carrying multiple mod functionalities pleasantly. Session outcomes (full
rationale + slicing in **`DESIGN-app-layout.md`** at repo root):
- **Minecraft/Prism PARKED** by Brian ("Java may not be best; Nexus + Thunderstore covers
  99%"). Future third-leg candidates ranked: mod.io > GameBanana > Steam Workshop.
- Principle locked: **games organize the app; sources are supply lines** (badges/filters/
  buttons, never top-level sections).
- Decisions: spine groups by GAME with a loadout flyout; Home becomes a DASHBOARD
  (jump-back-in tiles + recent-activity feed; health strip + news box deferred);
  "Get Mods" split-button per source; Library rolls up ONE row per mod with version
  expander + Installed badge (absorbs two §20.7 wishes); **R4 ships before the layout
  work**. Slicing: PRs L1 (quick wins incl. the tile-misalignment bug) → L2 (split-
  button) → L3 (Library rollup) → L4 (spine) → L5 (dashboard).

---

## 31. Session log — 2026-07-09 (cont.) — Rebrand R4: packaging — THE FIRST APPIMAGE

> Roadmap step 10. Branch `rebrand/r4-packaging`, **PR #23**. Verified end-to-end on the
> box: `Apocrypha.x86_64.AppImage` (234MB, self-contained) built, launched, opened Brian's
> real Library, and self-registered as the nxm/ror2mm handler.

### 31.1 What landed
- **The binary is `Apocrypha`**: `<AssemblyName>` in NexusMods.App.csproj (project/
  namespace names stay NexusMods.* per the Phase 1 §14 decision). Verified live:
  `WM_CLASS = "Apocrypha"` matches the desktop file's now-uncommented
  `StartupWMClass=Apocrypha`. Followers updated: `X-AppImage-Name`, uninstall.bat,
  uninstall-helper.ps1 (`Stop-Process -Name Apocrypha`), app.manifest win32 identity,
  docs (Uninstall.md exe names, Contributing.md CLI examples). No CI or code referenced
  the old exe name (checked).
- **pupnet conf**: `AppBaseName`/`PackageName = Apocrypha`; icon ladder from Brian's kit
  wired as `apocrypha.{16..512}.png` (PupNet REQUIRES `name.size.png` naming — the kit's
  `apocrypha-N.png` files are renamed on copy).
- **CI/metadata**: `release-nexus-mods` job + input deleted from release.yaml (notify
  jobs' `needs` fixed); `releases-to-appstream.py` OWNER/REPO → Jeagermeister/Apocrypha;
  NuGet.Build.props → Apocrypha authors/URLs/icon (dangling `Nexus-Icon.png` reference
  replaced by `Apocrypha-Icon.png` at repo root); docs landing icon → the in-repo
  apocrypha icon (NOT into `docs/Nexus` — that's the upstream mkdocs-theme SUBMODULE,
  briefly polluted and cleaned); `src/NexusMods.App/Deploy/` gitignored.
- CI reviewed: build-linux-pupnet.yaml builds PupNet from a pinned commit and needs no
  appimagetool on the runner; the AppImage job passes
  `-p DefineConstants=INSTALLATION_METHOD_APPIMAGE` (matched locally for verification).

### 31.2 Local-build quirks worth remembering
- PupNet 1.10.0 (dotnet tool) invokes the literal binary name
  `appimagetool-x86_64.AppImage`; the AUR package installs `appimagetool` → symlink
  needed for local builds. Brian installed `appimagetool` via paru (note: `libappimage`
  is a parsing library, NOT the tool).
- Each app start rewrites the desktop file's Exec to WHOEVER ran (source run vs
  AppImage) — last-runner-wins registration is upstream behavior; fine for a single
  user, worth revisiting before public release.

### 31.3 CI hygiene (follow-up PR #24, same day — Brian: "if we don't need this check, fix it")

Brian asked what the Networking Tests job was: upstream's live-API integration lane
(`RequiresNetworking=True` tests, self-hosted runner + `NEXUS_API_KEY`) — a ZOMBIE on the
fork (queued forever on every push; one cancelled). Audit found four workflows needing
upstream infra/secrets: networking_tests, mod_install_tests (NEXUS_API_KEY),
signing-test (ES_* signing creds), publish-nuget-packages (NUGET_KEY — would have FAILED
our first release tag). All four switched to `workflow_dispatch`-only with a fork-note
comment; lanes stay available if the fork ever stands up equivalents. Untouched:
clean_environment_tests (the real CI), pr/issue-maintenance bots, mkdocs (dormant,
main-branch-gated), git-builds, and the pupnet build/release chain.

### 31.4 Verification
Full solution 0 errors with the renamed binary; dev run + AppImage run both opened the
real datom store ("existing state found"); AppImage self-registered handlers (desktop
file Exec/TryExec point at the AppImage); WM_CLASS `("AppRun", "Apocrypha")` groups
correctly under the desktop entry. Windows packaging path (Inno setup, sign.bat,
registry) remains code-only — **needs a real-Windows QA pass** before any Windows
release.

---

## 32. ⏯️ RESUME POINTER — state at hand-off (2026-07-09) — newest session log: §35 (CI failure-email triage)

**🚀 APOCRYPHA v0.1.0 IS RELEASED:**
**github.com/Jeagermeister/Apocrypha/releases/tag/v0.1.0** — the AppImage (234MB,
prerelease flag, sha256 published), tagged on `linux-fork`, built locally with
`pupnet -v 0.1.0 -k appimage -p DefineConstants=INSTALLATION_METHOD_APPIMAGE` and
smoke-tested (opened the real Library) before upload. The binary version matches the
tag so the in-app updater stays quiet until a genuinely newer release exists. Merged
through PR #24 (R4 + CI hygiene); PR #25 (README Download section + this pointer +
OAuth queue entry) open.

Shipped, in order: Phase 1 (PRs #1–#9) → Phase 2 PR E (#10) → sync-wipe fix (#11) + PR F
rules engine (#12) → **APOCRYPHA** → rebrand R1 (#13) → repo rename → rebrand R2 (#14) →
handoff restore (#15) → PR H' runtime art (#16, §25) → build-health fix (#17, §25.3) →
**rebrand R3 (#18, §27)** → UI de-Nexus face (#19+#20, §29) → games search (#21, §29.1)
→ layout design locked (§29.2, #22) → rebrand R4 / first AppImage (#23, §31) → CI
hygiene (#24, §31.3) → **v0.1.0 RELEASED**.

**Next up (Brian's mode: one by one):**
1. **AUR `apocrypha` claim** (§23.1) + release cadence: future releases should go through
   the Release workflow (workflow_dispatch; Linux artifacts fine, Windows job untested —
   may need the sign step gated before the workflow path works end-to-end).
2. **OAuth session-expiry UX fix** (standing queue — Brian hits the cryptic log line at
   every launch until he re-logs into Nexus in-app).
3. **Layout epic** — `DESIGN-app-layout.md` PRs L1→L5 (quick wins incl. the MyGames
   tile-misalignment bug → split-button → Library rollup → spine grouping → dashboard).
4. **Phase 2 PR G** — RoR2 folds into the BepInEx family (§21 plan / design §9).
5. **Windows QA pass** — registry/ProgID changes (§27.1) + Inno/sign path (§31.3) on a
   real Windows box.
6. Community re-home becomes LIVE: move KIRO-HANDOFF/FABLE5-TASKS/DESIGN-* internal notes
   out of the repo before announcing anywhere (§20.7 note — they mention AI sessions).

Backlogs: §28.1 (UI — closed) and the standing queue at the end of §28.

## 33. Session log — 2026-07-09 (cont.) — Collections fix: the Subnautica popup storm

Brian installed a Subnautica collection: downloads fine, then one Advanced Installer
dialog **per mod** (all at once — `InstallCollectionJob` installs mods with
`Parallel.ForEachAsync`) and the collection left disabled. Two root causes, both ours:

1. **No collection fallback directory.** Collections suppress the advanced installer by
   design (upstream #2553, Vortex parity: unknown stuff → default folder), but only for
   games implementing `GetFallbackCollectionInstallDirectory` — upstream, only Stardew.
   `GenericBepInExGame` didn't → `FallbackCollectionDownloadInstaller.Create` returned
   null → `InstallLoadoutItemJob` line 92 `?? AdvancedManualInstaller` = the dialog.
2. **Nexus archives were never claimed.** `BepInExPluginInstaller.IsSupportedLibraryArchive`
   required Thunderstore identity or `manifest.json`. Nexus zips (what collections
   download) have neither → nearly every mod fell to the (null) fallback.

Fix (PR — `fix/collections-bepinex-fallback`): the family game now derives the fallback
dir from its schema default route (`InstallRuleRouter.DefaultRoute`, router instance now
shared game↔installer), and the plugin installer additionally claims archives carrying a
`.dll` anywhere or rooted in a route folder — so Nexus mods route *properly* (QMods,
state/subdir semantics intact) rather than landing flat in the fallback. RoR2 module
untouched (Thunderstore-only, no Nexus collections; folds into the family in PR G).
Tests: +2 router facts, +3 Subnautica e2e (Nexus no-manifest, game-root-packaged
no-nesting, fallback-dir). 56/56 BepInEx, 5/5 RoR2 pass; NexusMods.Collections.Tests
5 failures are pre-existing env-only (`NEXUS_API_KEY` live-API tests).

Follow-up queued: verify a real Subnautica collection end-to-end in the running app
(premium account = automated downloads; the earlier session-expiry caveat from the
OAuth queue entry still applies).

**QA CLOSED (same day): Brian installed the Subnautica collection end-to-end — no
popups — and reports downloads/installs/game-boot noticeably faster than Vortex on
Windows 11.** Collections work on Apocrypha; launch-announcement material.

## 34. Session log — 2026-07-09 (cont.) — Pre-0.1.1 sweep: OAuth expiry, L1, PR G

Brian's directive: knock out queue items 2/3/4 (OAuth UX, layout L1, PR G) + one ask
(make the Home-button tome fill its space), then cut v0.1.1. Three PRs:

**PR #27 — OAuth session expiry (`fix/oauth-session-expiry`).** `OAuth.RefreshToken`
now checks HTTP status: 4xx → new `OAuthSessionExpiredException` (error body logged);
5xx/network stay `HttpRequestException` so an outage can't log anyone out.
`OAuth2MessageFactory` catches it, drops the JWTToken like Logout does (retract →
LoginManager's datom observation flips the UI to logged-out → excise), and sends
`OAuthMessages.SessionExpired` (new, Abstractions.NexusWebApi) once — interlocked
flag, reset on live session. MainWindowViewModel: sticky Failure toast + “Log in”
button → `LoginAsync`. Two new strings in Language.resx (+ hand-added Designer
accessors — the PublicResXFileCodeGenerator is IDE-only). Tests: 400→typed throw,
500→transient. NEEDS LIVE QA: next launch with a stale session.

**PR #28 — L1 quick wins (`ui/l1-quick-wins`).** (1) MyGames mini-tile misalignment:
MiniGameWidget height varied with visible store rows; details block now reserves
3-row height (MinHeight=96 from BodyMD LineHeight 21) → uniform cards. (2) Home tome:
apocrypha-logo.svg viewBox cropped to the book's measured bright-pixel bounds
(74 88 892 892 — margins were ~90px dark padding) + spine icon Size 28→32 (ceiling:
32×√2 ≈ 45 < 48px hover ring) → visible book ~35% larger. NEEDS GUI LOOK.

**PR G (this branch, `feature/ror2-family-fold`) — RoR2 folds into the family (§21
plan / design §9).** `HandWrittenGames` exclusion emptied (kept as the seam);
`src/NexusMods.Games.RiskOfRain2` + its test project DELETED (App reference/
registration/sln pruned). Identity continuity verified three ways: schema row is
identity-preserving (settingsIdentifier RiskOfRain2 == GameId, displayName, exe,
Steam 632360 — locked in by the new `FamilyRow_PreservesHandWrittenIdentity` test);
the 5 hand-written installer tests ported verbatim to `RiskOfRain2InstallerTests`
through the family row (canonical rules); and the live headless boot lists Brian's
real RoR2 loadout cleanly against the 33GB store with the module gone. RoR2's
embedded art was byte-identical to the family placeholders (md5-verified) — no art
regression; riskofrain2 is a legacy community (no block in the schema) so like
Subnautica it keeps the placeholder THUMBNAIL but gets a real runtime-fetched cover
(parser reads covers from the instance's own meta.iconUrl — RegistrationTests
updated accordingly). Deltas vs hand-written: family row also carries Steam id
1180760 (RoR2 Dedicated Server — schema-consistent with other rows); run tool is
the family's `RunBepInExGameTool`. Suites: BepInEx 62/62, DataModel 232/232,
Library 3/3, Thunderstore 54/54, solution 0 errors.

**Next:** v0.1.1 once Brian merges + GUI-checks #27/#28 (Release workflow with the
untested-Windows-job caveat, or the known-good local pupnet path).

## 35. Session log — 2026-07-10 — CI triage: the three failure-email workflows

Brian got three "failed workflow" emails (Git Builds + Validate NuGet packages here,
Game Extension Test on the Vortex fork) and disabled all three as a stopgap. All
diagnosed and fixed; **merges are his** (auto-mode blocks self-merging PRs):

**Apocrypha — PR #31 (`ci/lfs-checkouts`), VERIFIED GREEN via branch dispatch.** Both
scheduled workflows died at our own guard: since 264dcd73c vendored the hashes DB via
git-lfs, any checkout without LFS smudging fails the pointer-file check.
networking_tests.yaml got `lfs: true` back then; git-builds' two pupnet workflows
(4 checkouts) and validate-nupkgs were missed. Fix: `lfs: true` everywhere, plus
validate-nupkgs triggers main→linux-fork (never fired here) and checkout v3→v4.
Bonus bug the CI run exposed: the guard's `<Touch>` runs BeforeTargets=PrepareForBuild
— before MSBuild creates obj dirs — so clean builds failed MSB3371 (local always had
obj/ already). Fixed with a `<MakeDir>`; clean local Release build + dispatched runs
confirm. **Both workflows re-disabled until #31 merges** (else tomorrow's crons fail
again off the unfixed default branch): after merge, run
`gh workflow enable git-builds.yaml && gh workflow enable validate-nupkgs.yaml`.

**Vortex fork — PR #6 (`ci/game-ext-test-fork-safe`), VERIFIED GREEN via dispatch.**
Nightly game-extension-test fails unconditionally on the fork: the harness hard-exits
without a `NEXUS_API_KEY` secret (repo has zero secrets), then rollup 410s filing its
rolling issue because issues are disabled on the fork. Fix: discover exports
`has_key` (secrets context is unusable in job-level `if`), test matrix skips with a
step-summary notice when absent, issue reporting wrapped in try/catch. Verified: run
green with test skipped. **Workflow re-disabled until #6 merges**; after merge
re-enable and it stays silently green — add a personal Nexus API key as the
`NEXUS_API_KEY` secret whenever the extension tests should actually run.

## 36. Session log — 2026-07-10 (cont.) — Roadmap step 11 MVP: share/join/update-nudge, live-verified

**Roadmap steps 11 (multiplayer mod sync) + 12 (gated P2P transfer, website-era) added to §8**
— PR #33 (docs-only, Brian merges). Step 12's guardrails are the design: never redistribute
permission-locked archives (Wabbajack precedent, API-revocation risk), P2P eligibility gated
per file, server relays signaling/manifests only, same-user device sync unrestricted.

**The MVP shipped in one session because upstream had already built ~70% and hidden it.**
Survey findings (Explore agent, verified by hand):
- `CollectionCreator` (src/NexusMods.Collections) has the full authoring pipeline: local
  collection → manifest → upload → publish revision (auto-changelog) → listed/unlisted toggle.
  `CollectionStatus.Unlisted = 0` is the enum default; collections are born unlisted.
- The complete share/publish UI existed on LoadoutPage behind
  `ExperimentalSettings.EnableCollectionSharing` (default false, "TODO: remove for GA").
- `CommandCopyRevisionUrl` was implemented but bound to no control (dead code).
- Friend side had NO in-app entry point: collection install required the website's Add
  button (nxm:// handoff). `NXMCollectionUrl` requires an explicit revision.

**PR #34 — branch `feat/loadout-share`, 3 commits:**
1. Sharing is GA: `EnableCollectionSharing` setting REMOVED (per upstream's TODO), share UI
   always on. Lesson: flipping the default wasn't enough — Brian's own
   `ExperimentalSettings.json` had frozen `false` from an earlier save; removal sidesteps
   every existing install. Copy-link button (+ toast) bound to the orphaned command.
2. Friend side: "Collection from link" in the Library Add dropdown (always visible — the
   Collections tab, where a second button lives, only renders once you HAVE a collection)
   → dialog → `CollectionUrlParser` (Abstractions.NexusWebApi, 23 tests; accepts
   www/next/no-scheme/games-prefix/revisions-suffix/nxm) → revision-less links resolve
   latest published via `IGraphQlClient.QueryCollectionRevisionNumbers` → constructed
   nxm:// URL handed to the SAME `NxmIpcProtocolHandler` the website uses (identical
   login/game checks + toasts + page-open behavior).
3. Update nudge: installed-collection page (CollectionLoadoutViewModel) now runs the same
   one-shot newest-revision check the download page had, shows "Update collection to
   revision N" in the status bar → opens the download page for the new revision.

**Live verification rig (Wayland/GNOME box, no sudo):** mutter EATS XTEST pointer injection
into the session's XWayland (:0) — clicks silently vanish; python-xlib + `import` alone are
useless there. Working rig: `Xwayland :99 -geometry 1600x900` (rootful, no `-rootful` flag on
this build — it's the default), launch app with `DISPLAY=:99 WAYLAND_DISPLAY=`, then XTEST
works natively. Dialogs are separate X windows — screenshot root or the dialog id, not the
main window. Typing needs a shift map (':' = Shift+';') — cost one false alarm where the
parser "failed" on `https;//`. Driver script: scratchpad drive.py pattern (session-local).
**Verified live:** Share button on My Mods toolbar (disabled-while-empty ✓), Add-menu entry,
dialog, invalid-link toast, and the full friend flow with a real revision-less link
(`next.nexusmods.com/stardewvalley/collections/htknoa`) → resolved rev 1 → manifest
downloaded → CollectionDownloadPage opened → "Collection added successfully" toast.
That collection was left in Brian's Stardew library deliberately (demo artifact; 1-click
delete). Kill stray app instances before relaunching — RocksDB LOCK errors mean one lives.

**Known gaps / next for step 11:** friend-side dedicated-loadout install (collection group
in active loadout is isolated enough for MVP; a "fresh loadout on join" option is phase 2);
manifest upload silently skips non-Nexus/non-downloaded items (`LoadoutItemGroupToCollectionManifest`
— needs a pre-upload warning); upload-path error handling is TODO-level upstream; my new
dialog/toast strings are literals, not Language.resx entries; free users still get per-mod
browser round-trips on Nexus (policy; Thunderstore via step 6 has no gate).

**Brian's new backlog item (2026-07-10):** per-row "update available" indicator on mod rows
with the NEW version number shown. Note: the Library page already has the update pipeline
(`ModUpdateService`, RefreshUpdatesCommand, Update All, per-row update actions via
`LibraryItemModel` update columns) — the ask is likely (a) surfacing an indicator + new
version on LOADOUT/collection page rows too, and (b) making the Library's existing
indicator more prominent (highlight/badge). Scope before building: check what
`ILibraryItemWithUpdateAction`/update columns already render per row.
