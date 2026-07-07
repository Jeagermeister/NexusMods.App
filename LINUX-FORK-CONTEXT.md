# NexusMods.App — Linux-First Hard Fork: Context & Starting Point

> Handoff doc written 2026-07-06 to bootstrap a fresh chat. Self-contained on purpose —
> a new conversation won't have the prior context. Read top-to-bottom once, then use the
> "First milestones" and "Key file map" sections as your working references.

---

## TL;DR — the decision

Brian (GitHub **Jeagermeister**, on **CachyOS/Arch**) is adopting the **archived** `Nexus-Mods/NexusMods.App`
as the base for an **independent, Linux-first hard fork** of a Nexus Mods mod manager. This supersedes
continuing to retrofit Linux support into his separate **Vortex** fork (`~/Source/Vortex`, TypeScript/Electron).

**Why this base:** it's C#/.NET (his stack), architected Linux-first, already ships the mature
Proton/Wine/Steam machinery he was hand-building in Vortex, and its event-sourced data model gives a
much higher capability ceiling. Nexus killed it (Jan 2026, repo archived ~Feb 20 2026) for *business*
reasons ("building every feature twice"), not technical ones — so the superior architecture is sitting
abandoned and uncontested.

---

## Who / goals (brief)

- **Stack:** Rust, Go, C++, **C#/.NET**, Python, TypeScript, SQL.
- **Goals (all of):** revive something he uses, build reputation/portfolio, commercial upside, enjoyment.
- **Stance:** independent hard fork (Linux-first identity; not dependent on Nexus).
- Companion Factorio/pack work exists but is separate.

---

## Why NexusMods.App over the Vortex fork (condensed)

The two paths have **different kinds of ceiling**:

- **Vortex fork** → higher *ecosystem/coverage* ceiling (86 game extensions in his fork today vs ~6 game
  families here), a *living upstream* he can rebase on, and low solo burden — BUT wrong stack for him
  (TS/Electron), Windows-first core, and Nexus is *expanding* official Vortex Linux support (SteamOS/Deck
  in 2026), which erodes the fork's reason to exist.
- **NexusMods.App revival** → higher *architecture/capability* ceiling, his exact stack, native Linux-first,
  an *uncontested* space (abandoned upstream) — BUT he owns 100% of all future work on a frozen codebase,
  narrow game coverage, and the ceiling is "team-shaped" (realistically wants a small community to reach).

**Decisive technical fact:** NexusMods.App is built on **MnemonicDB** — Nexus's own immutable,
event-sourced (Datom/Datomic-style) database. A mod setup is a **Loadout** that is versioned, diffable, and
revertable like git; deployment is a **3-way merge** (`SyncTree` diffing `DiskState` vs intended state).
That unlocks unlimited isolated loadouts, full undo/history, reliable "restore to vanilla", and reproducible
shareable setups — a class of features Vortex's Redux-state + deploy-method model can't reach without a core
rewrite. This is the main reason the ceiling is higher here.

---

## What you're inheriting (the architecture that matters)

- **Data model:** `NexusMods.MnemonicDB.*` — event-sourced attribute/datom store. **Do not rewrite this;
  it's the crown jewel.** Loadouts, transactions, reactive queries all sit on it.
- **Deployment engine:** `src/NexusMods.Abstractions.Loadouts.Synchronizers` — signature-based `SyncTree`,
  `DiskState`, 3-way reconcile. The hardest thing to build from scratch; already done.
- **Installer engines:** `NexusMods.Games.FOMOD` (+ UI), `NexusMods.Games.AdvancedInstaller` (+ UI).
- **Steam/Proton/Wine (Linux-first) infra — already present:**
  - `src/NexusMods.Backend/OS/LinuxInterop.Protocol.cs` — nxm:// registration (this is the ORIGIN Brian's
    Vortex `protocolRegistration/linux/nxm.ts` was ported from).
  - `src/NexusMods.Sdk/Games/Locators/ILinuxCompatabilityDataProvider.cs`, `NexusMods.Sdk/WineParser.cs`,
    `NexusMods.Backend/Games/Locators/{SteamLocator,WinePrefixWrappingLocator}.cs`.
  - `NexusMods.Games.Generic/{GameToolRunner,WineDiagnosticHelper}.cs` and protontricks dependencies
    (`AggregateProtontricksDependency`, `ProtontricksNativeDependency`, `ProtontricksFlatpakDependency`).
  - Per-game Proton diagnostics: Bannerlord `MissingProtontricksEmitter`, Cyberpunk
    `WinePrefixRequirementsEmitter` / `MissingProtontricksForRedModEmitter`.
- **Steam depot verification:** `NexusMods.Networking.Steam` (manifests/depots/CDN) — can match on-disk files
  to known releases (foundation for "detect/repair modified game files").
- **Game families present (~6 real games):** `CreationEngine` (Bethesda: Skyrim/Fallout etc.), `RedEngine`
  (Cyberpunk/Witcher), `Larian` (BG3), `MountAndBlade2Bannerlord`, `StardewValley` (+ `StardewValley.SMAPI`),
  plus `Generic`. **No Subnautica project exists** (see additive work below).

---

## Current repo state

- Location: `~/Source/candidates/modding/NexusMods.App` (blobless clone; full history, blobs on demand).
- Branch `main`, HEAD `48284f38c` (~Nov 2025, i.e. the frozen state before archival).
- Upstream is **archived/read-only** on GitHub. License: **GPL-3.0** (same as Vortex — code can be mixed).
- No fork remote yet; no personal branch yet.

## Environment / build & run

- **.NET SDKs installed:** 9.0.118 and 10.0.109. Target framework is **net9.0** → SDK 9 is correct; no install needed.
- **Build:** `dotnet build NexusMods.App.sln`
- **Run the app:** `dotnet run --project src/NexusMods.App` (Avalonia desktop UI). CLI lives at `src/NexusMods.App.Cli`.
- **Dev docs:** `docs/developers/Contributing.md`, `docs/developers/index.md`.
- Note: `src/NexusMods.App/NexusMods.App.csproj` sets `<OutputType>WinExe</OutputType>` but that's the
  Windows-RID branch; it builds/runs as a normal executable on Linux.

**First real test:** get it to build and launch on CachyOS, then manage ONE game end-to-end (Stardew Valley
is the simplest; a Bethesda title via CreationEngine is the highest-value) through the Proton/Wine path.
That establishes the true Linux baseline before any new work.

---

## What's genuinely additive from the Vortex fork (`~/Source/Vortex`)

Most of Brian's Vortex Linux work is *already covered here* (value flows reverse of what he assumed). The
genuinely NEW bits worth porting as *knowledge* (TS→C#, not verbatim):

1. **Subnautica + BepInEx** — no Subnautica game project exists here. His Vortex `game-subnautica` +
   BepInEx deploy logic can seed a new `NexusMods.Games.Subnautica`. Good "prove I can extend it" first game.
2. **CachyOS/Arch diagnostics** — NexusMods.App was tested mainly on SteamOS/Ubuntu; his Arch-specific
   setup knowledge (from "Improve CachyOS setup diagnostics") is new ground.
3. **Edge-case fixes as a QA checklist** — his `paths.proton.ts` (C:/Z: → Wine-prefix mapping),
   Hyprland/Sway/i3 xdg-utils fallback handling, and `LD_LIBRARY_PATH`/`ELECTRON_RUN_AS_NODE` unsets
   (the latter Electron-specific, less relevant here). Use to verify parity, not to copy.

Reference files in the Vortex fork: `src/main/src/filesystem/paths.proton.ts`, `src/main/src/adaptors.ts`,
`src/renderer/src/util/protocolRegistration/linux/{nxm,common}.ts`, `extensions/games/game-subnautica/`,
`extensions/games/game-mount-and-blade2/`, `docs/packaging/flatpak.md`.

---

## Hard truths / risks to plan around

- **Frozen codebase, no upstream.** Every future fix/feature/game is yours. Budget for that.
- **Narrow game coverage** (~6 vs Vortex's 86). Adding a game here is heavier (typed C# modules, not JS plugins).
- **The high ceiling is team-shaped.** Reaching NexusMods.App's potential solo is a multi-year arc; seeding
  a small community is the real unlock. Factor community-building into the plan, not just code.
- **GPL-3.0** — fine for OSS; matters if any commercial layer is later added (keep that layer separate).

---

## Suggested first milestones (v0 → v1)

1. **Baseline:** build + launch on CachyOS; manage one game end-to-end via Proton. Document what works/breaks.
2. **Re-home the fork:** new name/identity, own git remote, Linux CI, README with the "Linux-native,
   loadout-based, revival-of-what-Nexus-abandoned" narrative. Note GPL-3.0 provenance.
3. **nxm wiring:** confirm `LinuxInterop.Protocol.cs` registration works on Arch/CachyOS (and decide how the
   standalone **nxmproxy** companion — see below — fits, if at all).
4. **First new game = Subnautica/BepInEx:** proves the extension path and fills a real gap.
5. **CachyOS/Arch diagnostics** pass.
6. **Community seeding:** publish, write up the architecture story, invite contributors.

## Open decisions to make early

- **Name/brand** for the independent fork.
- **First proof game** (Stardew = easiest; Bethesda = highest value).
- **Solo pace vs. active community-seeding** (this largely determines whether the ceiling is reachable).
- **nxm handling:** rely on the built-in `LinuxInterop.Protocol.cs`, or route via the standalone nxmproxy tool?
- Keep MnemonicDB + Synchronizer **as-is** (recommended) — build on them, don't touch the core.

---

## Key file map (where the important code is)

| Concern | Path |
| --- | --- |
| Event-sourced datastore | `NexusMods.MnemonicDB.*` (package refs throughout `src/`) |
| Loadout model | `src/NexusMods.Abstractions.Loadouts/` |
| **Deployment engine (3-way sync)** | `src/NexusMods.Abstractions.Loadouts.Synchronizers/` |
| nxm:// Linux registration | `src/NexusMods.Backend/OS/LinuxInterop.Protocol.cs` |
| Wine/Proton compat data | `src/NexusMods.Sdk/Games/Locators/ILinuxCompatabilityDataProvider.cs`, `src/NexusMods.Sdk/WineParser.cs` |
| Steam / prefix locators | `src/NexusMods.Backend/Games/Locators/{SteamLocator,WinePrefixWrappingLocator}.cs` |
| protontricks deps | `src/NexusMods.Games.Generic/Dependencies/` |
| FOMOD / advanced installer | `src/NexusMods.Games.FOMOD/`, `src/NexusMods.Games.AdvancedInstaller/` |
| Game modules (pattern to copy) | `src/NexusMods.Games.StardewValley/`, `src/NexusMods.Games.MountAndBlade2Bannerlord/` |
| App entrypoint / UI | `src/NexusMods.App/`, `src/NexusMods.App.UI/`, `src/NexusMods.App.Cli/` |

---

## Companion tool: nxmproxy (already ported to Linux)

Separate small Rust tool at `~/Source/candidates/modding/nxmproxy`, branch **`linux-port`** (working,
**uncommitted**). It routes `nxm://` links to the right manager per-game. Its Linux port was done in this
session (xdg-settings + .desktop + Unix-socket IPC, mirroring Brian's validated Vortex nxm logic; green
build, tests pass, verified end-to-end). It's optional for this project since NexusMods.App has its own
`LinuxInterop.Protocol.cs`, but it's a ready-made multi-manager nxm router if wanted.

## Related local paths & memory

- This fork base: `~/Source/candidates/modding/NexusMods.App`
- Vortex fork (reference for additive bits): `~/Source/Vortex`
- Candidate index + rationale: `~/Source/candidates/CANDIDATES.md`
- nxmproxy companion: `~/Source/candidates/modding/nxmproxy` (branch `linux-port`)
- Persistent memory: `~/.claude/projects/-home-sirjeager-Source/memory/` (see `project-takeover-candidates.md`).
