# Changelog

Apocrypha's own release history. Entries are added when a release is published — see
[GitHub Releases](https://github.com/Jeagermeister/Apocrypha/releases) for the full text
(including download links and checksums) of every version below.

# v0.3.0 2026-07-13

**mod.io arrives, in royal purple.** A new mod source, a finished collections workflow, and a new look.

### mod.io is here (Phase 1 — Baldur's Gate 3)

Apocrypha's third mod source, joining Nexus Mods and Thunderstore. BG3 is mod.io-native, and now so are we:

- **Paste a link, get a mod**: Library → Add ▸ *Mod from mod.io link* → paste any `mod.io/g/baldursgate3/m/…` link and the mod's latest file lands in your Library, ready to install through the existing BG3 installer.
- Bring your own (free) API key from [mod.io/me/access](https://mod.io/me/access) — the app prompts you on first use. Read-only access; the key stays on your machine.
- *From mod.io* joins the per-source "Get Mods" entries, and `modio download` / `modio set-api-key` verbs cover the CLI.
- Experimental toggle in Settings; more mod.io games to follow.

### Move mods between collections

- **Move to collection ▸** on every installed mod's menu — reparent a mod into any of your editable collections in one click.
- Mods delivered by a Nexus collection stay with their collection (the menu explains why instead of dead-clicking).
- The Library's install-target picker is now labeled **Install to:** so it's clear where new installs go.

### Royal purple

The primary accent — Apply, Log In, Add game, Install, progress bars — trades Nexus orange for a deep royal purple.

### Fixes

- mod.io API calls follow the platform's migration to `modapi.io` game subdomains.
- Collections column no longer goes stale after moving a mod.

# v0.2.2 2026-07-12

**One-click modpacks.** Click **Install with Mod Manager** on a Thunderstore modpack and it just works — verified live with a 273-dependency Risk of Rain 2 pack.

### One-click Thunderstore modpacks

- `ror2mm://` install links now resolve a modpack's full dependency closure against the community package index in **seconds** (previously: minutes of silent per-package API calls).
- Packages download **in parallel** and show up on the Downloads page; a toast tells you how many mods are on the way.
- Re-clicking a link **resumes** a partial install instead of doing nothing.

### No more duplicate downloads

- Clicking an install link twice while the first click is still working now answers "Already downloading" instead of downloading everything again.
- Overlapping installs (two modpacks sharing dependencies) download each package exactly once.
- New **Remove duplicates** button on the Library toolbar.

### Fixes

- Saved window layout no longer gets discarded at startup after leaving a game-specific Downloads page open.

# v0.2.1 2026-07-11

**Optional mods, found.** Collections often ship optional mods alongside the required ones — but after installing a collection, the app never told you they existed. This release is for that.

### Find and install a collection's optional mods

- The installed-collection page now shows a **View optional mods (N)** button in the toolbar whenever the collection offers optional mods.
- After a collection finishes installing, the confirmation toast tells you how many optional mods are available and where to find them.

# v0.2.0 2026-07-11

**Collections + your mods, together.** The biggest release yet: the app is now Apocrypha all the way down — every project, namespace, and assembly — and modding with collections finally works the way you'd expect.

### Highlights

- **Collections + your own mods, together.** Install a collection, then add whatever you want beside it: your own mods land in "My Mods" with working toggles, mixed rows show a live switch for *your* copy of a mod the collection also ships, and blocked toggles explain themselves instead of silently doing nothing.
- **A Library that knows which game you're modding.** Thunderstore mods are now scoped to their games via community listings — no more Risk of Rain 2 mods showing up in Subnautica's library.
- **Get Mods, per source.** The Library's "Add" menu now offers each mod source the game actually has.
- **Real art everywhere.** Games from legacy Thunderstore communities now show their cover art instead of a placeholder.

### Fixes & polish

- One set of window controls on Linux (the native title bar wins; no more doubles).
- Protocol handlers can no longer be clobbered into a broken state by framework-dependent launches — fixes OAuth logins silently failing.
- Internal rename: `NexusMods.*` → `Apocrypha.*` across all 98 projects, with databases, saved workspaces, and settings carrying over untouched.

# v0.1.2 2026-07-11

**Share your loadout, now on Windows too.** The first release with Windows support — Apocrypha now ships for Linux and Windows.

### Share your loadout with a link

- **Share** your mod list from the My Mods toolbar: one click uploads it as an unlisted collection and copies a link.
- **Join** a friend's setup: paste their link via *Library → Add → Collection from link*.
- **Stay in sync**: installed collections check for newer revisions and offer a one-click "Update collection" jump.

### Update indicators everywhere

- Installed-mods pages now have a **Version column** showing each mod's installed version and a `current → new` arrow when an update is out.

### Under the hood

- Full CI on every change: build + test on Linux and Windows, packaging checks for all release artifacts.
- macOS support removed — Apocrypha targets Linux and Windows.

# v0.1.1 2026-07-10

**Collections work.** The first update after launch — collections, session handling, and the last structural piece of the BepInEx game family.

### Fixed

- Nexus Mods collections now install correctly for BepInEx-family games (Subnautica and ~200 others).
- Expired Nexus Mods sessions are handled cleanly — a toast with a Log in button instead of a silent failure.
- My Games tiles align uniformly regardless of how many stores support each game.

### Changed

- The Apocrypha tome fills the Home button properly.
- Risk of Rain 2 is now served by the schema-driven BepInEx family like every other Thunderstore game.

# v0.1.0 2026-07-10

**The first release.** Canon is just the beginning. This is the first packaged release of Apocrypha, the Linux-first mod platform — an independent continuation of the archived Nexus Mods App.

### What's inside

- **Two mod sources, one Library**: Nexus Mods (one-click, collections, API) and Thunderstore (one-click, dependency resolution) side by side in the same loadouts.
- **Hand-tuned games**: Stardew Valley, Cyberpunk 2077, Baldur's Gate 3, Skyrim SE, Fallout 4, Bannerlord, Risk of Rain 2 — plus the BepInEx family.
- **Loadouts work like git**: every change is a revision — diff it, revert it, keep parallel loadouts of the same game.
- **Linux is first-class**: automatic Proton `winhttp.dll` overrides for BepInEx games, xdg protocol registration, local login-free game-version recognition via Steam depot manifests.
- **No telemetry.** The upstream phone-home was removed entirely.
