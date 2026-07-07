# CachyOS Baseline — Milestone 1 Findings (2026-07-06)

First build + launch of the frozen codebase (`48284f38c`) on CachyOS/Arch, .NET SDK 9.0.118.

## What works

- **Full solution builds clean**: 96 projects, 0 errors (~38s cold, ~20s warm).
  Requires `git submodule update --init --recursive` first (SMAPI + docs submodules).
- **App launches and runs** (Avalonia UI, Wayland). Startup ~2s.
- **Linux interop works out of the box**: `xdg-settings` nxm:// handler registration,
  `.desktop` file creation at `~/.local/share/applications/com.nexusmods.app.desktop`,
  MIME cache update — all succeeded on first run.
- **Steam locator works**: found both library folders (incl. second drive at
  `/mnt/.../SteamLibrary`), parsed all app manifests, and detected
  **Mount & Blade II: Bannerlord** (appid 261550) as a managed-game candidate.
- Games hash database fetched and opened; MnemonicDB datom store bootstrapped.

## What broke (and status)

1. **FIXED — startup crash in Debug builds** (`LocalMappingCache.cs:74`).
   `games.json` is downloaded from Nexus's live CDN at build time
   (`https://data.nexusmods.com/file/nexus-data/games.json`). Live data now has two
   domain collisions the Nov-2025 code never saw: id 8703 "Neverwinter" vs id 180
   "Neverwinter Nights" (both `neverwinter`), and id 9193 "WRC 5" vs id 2892
   (both `wrc5`). A `Debug.Assert` assumed the id↔domain mapping is 1:1 and killed
   the process. Replaced with a warning log; first-entry-wins dedupe unchanged.
   *(Uncommitted — first fork patch.)*
   **Follow-up risk:** build-time dependency on a live Nexus URL is a fragility for an
   independent fork — vendor a snapshot of games.json eventually.

2. **WARN — protontricks not installed** (`Protontricks: couldn't query information`).
   Available in Arch `extra` (1.14.1-1): `sudo pacman -S protontricks`.
   Needed for Bannerlord/Cyberpunk Proton diagnostics.

3. **WARN — `df --output=source` fails** on `~/.local/share/NexusMods.App/DataModel/Archives`
   (`LinuxInterop.GetFileSystemMount`), likely because the directory doesn't exist on
   first run. Non-fatal; worth a guard + mkdir check later.

4. **PATCHED — "Cannot backup 9560 files / 30GB > 5GB max" when managing Bannerlord.**
   Root cause chain (found live, 2026-07-06):
   - The file-hashes DB comes from `Nexus-Mods/game-hashes` GitHub releases — **last
     release 2025-09-30, frozen forever** (fetched by `FileHashesServiceSettings`).
   - Bannerlord has been patched since, so its current Steam manifest IDs aren't in
     the DB → `Unable to find game version` → `GetGameFiles` can't classify anything
     as a known/restorable game file → ALL 9,560 files (30.2GB) flagged `BackupFile`.
   - That trips the hardcoded 5GB fuse in `ALoadoutSynchronizer.MaximumBackupSize`
     (`ALoadoutSynchronizer.cs:43`, comment was stale, said 2GB).
   Patch: raised fuse to 100GB (uncommitted). Full-size backups are acceptable
   short-term (3.6TB free) and the archive store compresses/dedupes.
   **Strategic consequence: the fork must own the game-hashes pipeline.** Every game
   updated after Sept 2025 will look fully-modified until we can generate hash-DB
   releases ourselves (Steam depot manifests via `NexusMods.Networking.Steam` are the
   likely source). This is a bigger deal than any single game module.

## Baseline test-game decision input

Both **Bannerlord** and **Stardew Valley** are installed (Steam, second drive) and
detected by the locator. Bannerlord exercises the Proton-diagnostics path (needs
protontricks); Stardew is the simplest end-to-end path. Login + general UI navigation
confirmed working (2026-07-06).

## Vortex migration notes (2026-07-06)

- **No Vortex importer exists** in the codebase (the only "vortex" refs are
  collection-JSON format compat). Migration is manual.
- **Brian's Bannerlord install has live Vortex-deployed mods** (12 mod modules +
  `vortex.deployment.json` in `Modules/`). Deploying Vortex was likely the old
  Windows install — no Vortex data dirs found on the Linux side or the games drive.
- **Order of operations matters:** purge/remove Vortex-deployed files BEFORE managing
  the game in the app, or the mods get ingested/backed up as if they were game state.
  `vortex.deployment.json` lists every deployed file → can script a clean removal if
  Vortex itself isn't available to purge.
- Mod *archives* can be brought in via Library → drag-drop / add-from-drive
  (`ILibraryService.AddLocalFile`). FOMOD choices, profiles, load order don't transfer.
- **FORK FEATURE IDEA (high-value differentiator): "Import from Vortex" wizard** —
  parse Vortex state/deployment manifests + downloads folder, auto-populate Library
  and a loadout. Brian owns a Vortex fork and knows the state format cold. This is
  THE adoption feature for migrating the orphaned Vortex-on-Linux userbase.

## Known UI warts

- Sporadic `[ERROR] unhandled exception in R3 ... ObjectDisposedException` on panel
  close — reactive teardown race, non-fatal.

## Run commands

```sh
dotnet build NexusMods.App.sln
dotnet run --project src/NexusMods.App     # UI
```
