# FABLE5-TASKS — high-leverage work brief for a long, capable agent session

> Written 2026-07-08 by Kiro for a follow-on Claude Code (Fable 5) session on the NexusMods.App
> **Linux-first hard fork**. This is a *candidate menu*, not a mandate — pick per Brian's priorities.
> Read the three companion docs first for full context:
> - `KIRO-HANDOFF.md` — everything done so far (sessions 10–14), current state, directives §6/§7.
> - `LINUX-FORK-CONTEXT.md` — fork rationale, architecture map, milestones, what's "crown jewel".
> - `BASELINE-CACHYOS.md` — first-build/first-run findings + root causes.

---

## 0. Orient first (do this before touching anything)

- Repo: `~/Source/NexusMods.App`, dev branch **`linux-fork`** (origin = `Jeagermeister/NexusMods.App`,
  upstream = archived `Nexus-Mods/NexusMods.App`). **PR #3 is open** (in-app "Recognize installed version"
  + re-included recognition backend) — confirm its state before branching.
- One-time: `git submodule update --init --recursive` (SMAPI + docs, build fails without).
- Build: `dotnet build NexusMods.App.sln` (96 projects). Baseline is **0 errors, ~43 pre-existing warnings**.
- Run UI: `dotnet run --project src/NexusMods.App`. CLI verbs: `dotnet <NexusMods.App.dll> as-main <verb…>`.
- **Ground rules that must hold for every change:**
  1. **Do not rewrite MnemonicDB (`NexusMods.MnemonicDB.*`) or the synchronizer
     (`src/NexusMods.Abstractions.Loadouts.Synchronizers`).** Build on them. This is the moat.
  2. Keep the build green (0 errors) and don't *add* warnings. Prefer to reduce them.
  3. Linux-first. GPL-3.0 throughout (Vortex/r2modman code+formats can legally flow in as knowledge).
  4. Commit only with explicit go-ahead; push feature branches, PR into `linux-fork` (never `main`/upstream).

### Why hand these to a big-context, long-running agent specifically
Prioritize work that this environment (Kiro) could **not** fully do and that rewards deep, cross-file
reasoning + real verification:
- **Runtime-verify the Avalonia UI** (Kiro is headless — e.g. the recognition button in PR #3 was never
  launched). Actually run the app, click through flows, watch logs.
- **Whole-repo refactors / new subsystems** where holding 20+ files in context at once matters (the §7
  platform work below).
- **Full test-suite runs** and iterating to green across the 96-project solution.

---

## 1. On the "complete code refactor" idea — read this before doing it

A blanket rewrite here would be a mistake and I'd advise against it. The codebase is a *mature, well-
architected* upstream that Nexus abandoned for business (not technical) reasons; its core is the reason the
fork exists. A full refactor would burn the whole session re-deriving working code and risk the one asset
that's hard to rebuild. Instead, do **targeted, high-leverage refactors** (§4) and put the horsepower into
**new capability** (§2) and **hardening** (§3, §5).

If you want a "big model" refactor that's actually safe and valuable: pick **one** subsystem with real
smell (candidates in §4), write a short design note, refactor behind existing tests, and keep the public
surface stable.

---

## 2. Flagship features (biggest payoff; deep multi-file work)

### 2.1 Directive §7 — the mod-source abstraction + Thunderstore/BepInEx (the fork's identity)
The North Star (see `KIRO-HANDOFF.md` §7): one Linux app that manages Nexus games, Thunderstore/BepInEx
games (Lethal Company, Valheim, RoR2…), and eventually Minecraft — same Library, same loadout/undo model.
- **Phase 1 (do first):** generalize "a mod source" so Thunderstore's API sits beside the Nexus API as a
  peer — search/metadata/versioning/dependency-resolution/download + `ror2mm://` one-click. The core
  abstractions are already source-agnostic in the right places (`ILibraryService`, Loadouts,
  `ILibraryItemInstaller`, `IJob`); Nexus is currently privileged (`NexusModsLibrary`, nxm handling,
  collections). Extract the "mod source" seam.
- **Phase 2:** a generic **BepInEx game family** module (install loader, deploy to `BepInEx/plugins`,
  per-loadout profiles) — covers dozens of Unity/Thunderstore games at once and bridges to Subnautica (§5.4).
- **Approach for a long session:** land a **design doc first** (`docs/` or a `DESIGN-modsources.md`),
  reviewed before code, then implement Phase 1 behind tests. Modrinth-first for Minecraft mods later
  (API-key-free; CurseForge's ToS is the licensing minefield — defer it).
- **Suits a big model because:** it's an architecture-defining change touching Library, downloads,
  installers, protocol handlers, and UI simultaneously.

### 2.2 Own game-hashes pipeline — the *central* (Path A) half + flip remote updates back on
The fork already has **local, login-free recognition (Path B)** — see `KIRO-HANDOFF.md` §11–§14 (a user
recognises their installed version by hashing on-disk files vs Steam depot manifests). What's still missing
is the fork-owned *feed* so recognition works without every user re-indexing:
- Build a CI job that produces `game_hashes_db.zip` + `manifest.json` for current game versions (reuse the
  in-tree `steam app index` producer + `game-hashes-db build` builder; Steam depot manifests are the source).
- Publish it to a fork-owned release feed.
- Then honor the **§10.2 reminder**: flip `FileHashesServiceSettings.EnableRemoteUpdates` default back to
  `true` and repoint `GithubManifestUrl` / `GameHashesDbUrl` from `Nexus-Mods/game-hashes` to the fork feed
  (both carry `TODO(linux-fork)` markers).
- **Also finish the Path B follow-ups** (KIRO-HANDOFF §14.5): localize the recognition UI strings, add a
  determinate progress bar (recognizer already emits `IProgress<double>`), and handle DLC depots whose
  manifests aren't in depotcache.

### 2.3 "Import from Vortex" wizard (killer adoption feature)
Parse Brian's Linux Vortex fork data (`~/.config/@vortex/main/`: `vortex.deployment.json`, staging dirs,
`downloads/<game>/`) → auto-populate the Library and a Loadout. Brian owns the Vortex fork and knows the
formats, so pairing with him is high-bandwidth. Great "why switch to us" story. (`KIRO-HANDOFF.md` §4.4.)

---

## 3. Linux hardening (the fork's whole reason to exist)

### 3.1 protontricks → umu-launcher (drop a fragile dependency)
`protontricks` is only needed when the app runs a Windows `.exe` inside a game's Proton prefix
(Bannerlord BLSE via `GameToolRunner`/`BannerlordRunGameTool`, Cyberpunk RedMod deploy). Evaluate replacing
it with `umu-launcher`/direct Proton invocation and dropping the protontricks native-dependency chain
(`src/NexusMods.Games.Generic/Dependencies/`). Deploying mods needs no Wine at all. High Linux-UX payoff.

### 3.2 CachyOS/Arch diagnostics pass
Upstream was tested mainly on SteamOS/Ubuntu. Do an Arch/CachyOS parity pass using Brian's Vortex QA
knowledge (C:/Z: → Wine-prefix mapping in `paths.proton.ts`; Hyprland/Sway/i3 xdg-utils fallbacks). Verify
`nxm://` registration (`src/NexusMods.Backend/OS/LinuxInterop.Protocol.cs`) end-to-end on this box.

### 3.3 Known Linux warts (small, satisfying)
- `df --output=source` fails on the not-yet-created Archives dir → `LinuxInterop.GetFileSystemMount`
  (guard for missing dir).
- Sporadic R3 `ObjectDisposedException` on UI teardown (also seen as the benign `nxm`/xdg-settings
  `OperationCanceledException` at CLI shutdown) — trace and quiet it.
- Bannerlord `MissingProtontricksEmitter` calls `CreateMissingProtontricksForRedMod` — upstream copy-paste
  bug (message names the wrong game). One-line fix.

---

## 4. Targeted refactors (safe, high-leverage — NOT a rewrite)

- **`FileHashesService` complexity.** It now carries the read-only shipped DB + a writable local overlay
  unioned into every read path. It's grown large. Consider extracting the overlay/union into a small
  collaborator and adding the integration test that was deferred (see §6). Keep behavior identical.
- **Generalize local recognition beyond Steam.** `ILocalGameVersionRecognizer` + `SteamDepotCachePath` are
  Steam-specific. GOG/Epic also have on-disk manifest data; a store-agnostic recognizer seam would be a
  clean generalization once a second store needs it (don't build it speculatively — wait for the 2nd case).
- **Recognition idempotency refinement.** The writer currently skips a manifest already in the overlay
  (correct + safe). If richer "replace/update" semantics are ever needed, add retract-by-`ManifestId`.

---

## 5. New game module + coverage

### 5.4 Subnautica / BepInEx (milestone 4 in LINUX-FORK-CONTEXT)
No Subnautica project exists. Seed `NexusMods.Games.Subnautica` from Brian's Vortex `game-subnautica` +
BepInEx deploy logic (port as *knowledge*, TS→C#). Copy the pattern from
`src/NexusMods.Games.StardewValley/` or `.../MountAndBlade2Bannerlord/`. This proves the extension path and
directly bridges into §2.1 Phase 2 (generic BepInEx family).

---

## 6. Quality & build health (great "warm-up" work for a fresh session)

- **Root-cause the source-generator failure.** Every build emits `CSC : warning CS8785: Generator
  'GenerateWeaveSources' failed to generate source … ArgumentNullException … (Parameter 'path2')` across
  many projects. It's non-fatal today but it's a real generator crash worth fixing (or pinning/patching).
- **Clear the ~43 warnings:** duplicate usings (`NexusMods.Sdk/Loadouts/Models/Loadout.cs` CS0105),
  obsolete-API usages (CS0618), unused vars (CS0168 in `BuildHashesDb.cs`), unreachable code (CS0162 in the
  disabled `NoWayToSourceFilesOnDisk` emitter). Small, mechanical, improves signal.
- **Tests to add** (fork features currently thin on coverage):
  - `FileHashesService` overlay-union + idempotency integration test (deferred in §13.4 because it needs the
    full MnemonicDB/query-engine DI graph stood up — a good task for a session that can run the full suite).
  - Synchronizer "silent backup skip @ 5GB" (directive §6) behavioral test.
  - `LocalGameVersionRecognizer.RecognizeAsync` across multiple depots (mock/stub the reader+hasher).
- **Runtime-verify PR #3's GUI.** Launch the app on a game whose version is unknown (e.g. a Bannerlord depot
  not yet indexed), confirm the "Recognize installed version" button appears, click it, watch the toast and
  that it hides once recognised. Kiro could not do this headlessly.

---

## 7. Re-home / community (from the roadmap; not code-heavy but high-impact)

Name/brand the fork, add Linux CI, write the README narrative ("Linux-native, loadout-based, revival of what
Nexus abandoned", GPL-3.0 provenance), then seed a small community. `LINUX-FORK-CONTEXT.md` notes the real
ceiling is "team-shaped" — this matters as much as any feature.

---

## Suggested ordering for one long session
1. **Warm up** on §6 (build health + a couple of tests) to get the suite green and build the mental model.
2. **Runtime-verify PR #3** (§6 last bullet) — closes the one thing Kiro couldn't.
3. Then pick ONE flagship: **§2.1 Phase 1** (design doc → Thunderstore source) is the highest-identity bet;
   **§2.2** (hash pipeline + flip remote updates) is the highest "make the existing feature real" bet;
   **§3.1** (protontricks→umu) is the highest pure-Linux-UX bet.

Keep each unit of work on its own branch → PR into `linux-fork`, and update `KIRO-HANDOFF.md` with a new
session-log section as you go (that's how continuity has been maintained across every session).
