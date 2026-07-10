# DESIGN — App layout for multi-source modding

> Decisions from Brian's live design session, 2026-07-09 (with Claude Code / Fable 5).
> Scope: how the app's navigation and core surfaces carry MULTIPLE mod sources per game
> pleasantly — organized for today's two (Nexus Mods, Thunderstore) and extensible to a
> future third without rearranging furniture. Companion docs: `DESIGN-modsources.md`
> (source architecture, §15 multi-source principle), `DESIGN-bepinex-family.md`.

## 1. The organizing principle

**Games organize the app; sources are supply lines.** Sources appear as badges, filters,
and download buttons — never as top-level navigation sections. A user thinks "I'm modding
Subnautica", not "I'm browsing Thunderstore". Every decision below follows from this.

## 2. Decisions (Brian, 2026-07-09)

1. **Minecraft/Prism: PARKED.** The Java/MSA-auth/launcher stack isn't worth the scope;
   Nexus + Thunderstore covers ~99% of our supported games' needs. Future third-leg
   candidates, in value order: **mod.io** (open REST API; already the official platform
   for games we support — Baldur's Gate 3, Deep Rock Galactic), **GameBanana** (API,
   niche communities), **Steam Workshop** (huge but walled; gray-area access). The Prism
   clone stays at `~/Source/candidates/modding/PrismLauncher` (GPL-3.0, compatible) if
   this ever revives.
2. **Spine: group by GAME**, one button per managed game (its tile art); games with
   multiple loadouts get a flyout (loadout list + "New loadout"). Replaces one-button-
   per-loadout, which doesn't scale past a handful of experiments.
3. **Home (the tome button) becomes a DASHBOARD**: jump-back-in game tiles with sync
   state + a recent-activity feed. (Considered and deferred: health/warnings strip,
   release-news box — revisit post-R4 when releases exist.)
4. **"Get Mods" is a split-button** listing the game's actual sources with brand icons;
   single-source games get a plain button. A future source is just another row.
5. **Library: one row per MOD, rolled up** across downloaded versions (expander shows
   versions; badge shows source; "Installed" badge shows loadout membership). Absorbs
   the §20.7 wishes (one-row-per-package + Installed badge).
6. **Sequencing: R4 (packaging/AppImage) ships FIRST**, then the layout work as the
   flagship post-release improvement.

## 3. Spine (decision 2)

- One `ImageButton` per MANAGED game, ordered by last activity; detected-but-unmanaged
  games stay on My Games only.
- Click = open the game's last-active loadout workspace. A chevron affordance (hover or
  long-press) opens the loadout flyout: loadouts by name + apply state, "+ New loadout".
- Single-loadout games skip the flyout entirely on click.
- Home (tome) and Downloads keep their fixed slots (top/bottom).

## 4. Home dashboard (decision 3)

- **Jump back in:** managed-game tiles (real covers via the PR H' pipeline) with sync
  state (✓ synced / ⚠ unapplied changes / ● busy) — click through to the workspace.
- **Recent activity:** downloads, installs, applies across all games; each row deep-links
  to the loadout/Library page it touched. Feed derives from MnemonicDB transactions +
  job history — no new persistence.
- My Games (add/manage) remains its own page, one click away; the dashboard links to it.

## 5. "Get Mods" split-button (decision 4)

- Source list per game comes from what the game actually has: a Nexus game id
  (`IGame.NexusModsGameId`), a Thunderstore community (`BepInExGameData.CommunitySlug` /
  RoR2), future capabilities as they land.
- Buttons open the game's page on that source's site — the established website+protocol
  model (`nxm://`, `ror2mm://` one-click flows back into the app). An in-app browse page
  stays a NON-goal for now (Nexus ToS routes downloads through their site regardless;
  revisit if/when a fully-open-API source like mod.io lands).

## 6. Library rollup (decision 5)

- One row per mod package; versions nest under an expander, newest first, loadout-member
  versions marked. Source badge per row; "Installed" badge when any version is in the
  current loadout.
- Identity stays per-source `(source, native id)` — NO fuzzy cross-source merging (the
  DESIGN-modsources §15 rule). A mod that exists on both Nexus and Thunderstore is two
  rows; update-checking may justify linking them later.
- Thunderstore mod icons can ride `CachedHttpStreamFactory` (PR H' infrastructure) as
  part of this work.

## 7. Implementation slicing (all AFTER R4; each independently shippable)

- **PR L1 — quick wins:** MyGames tile-misalignment fix (standing-queue bug) + any
  low-hanging polish. Can ride anytime, even before R4.
- **PR L2 — Get Mods split-button** (smallest structural piece, immediately useful for
  dual-source games like Subnautica).
- **PR L3 — Library rollup + Installed badge + mod icons.**
- **PR L4 — spine game-grouping + loadout flyout.**
- **PR L5 — Home dashboard** (largest new surface; lands last on purpose — it showcases
  everything the earlier slices built).

## 8. Open questions (decide during implementation)

- Flyout interaction: hover-flyout vs click-chevron vs right-click (test on the box; must
  feel right under both mouse and touchpad).
- Activity feed depth/retention and whether applies collapse into one entry per batch.
- Whether the dashboard's game tiles replace or duplicate the My Games "detected" grid
  for small libraries (≤3 games).
