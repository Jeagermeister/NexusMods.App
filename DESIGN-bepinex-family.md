# DESIGN — Phase 2: The generic BepInEx game family

> Written 2026-07-08 (Claude Code / Fable 5). Companion to `DESIGN-modsources.md` (Phase 1,
> shipped as PRs #6–#9) — this doc executes that design's §8 promise: *"Phase 1 implements the
> common-case rules by hand; Phase 2 swaps in schema-driven installRules."* Research inputs:
> a full analysis of the Thunderstore **ecosystem schema** (dataset + published JSON-Schema +
> r2modmanPlus's consumption of it) and a seam-by-seam map of what is actually game-specific
> in the Phase 1 RoR2 module. Status: **draft for Brian's review** — open decisions in §14.

---

## 1. Goal and non-goals

**Goal:** turn the hand-written Risk of Rain 2 module into a **data-driven family** so that
supporting a new Thunderstore/BepInEx game is a *data row*, not a C# project. Ship a curated
starter set of real games (Subnautica first — it's installed on the dev box and dual-source),
with install routing driven by the ecosystem schema's `installRules` instead of hardcoded
categories.

**Non-goals (this phase):**
- Non-BepInEx loaders (MelonLoader, Shimloader, GDWeave, return-of-modding, godotml, …).
  These rely on imperative installer code in r2modman, not declarative rules. Scoping to
  `packageLoader == "bepinex"` covers **211 of 296 schema games** and dodges all of it.
- Dedicated-server instances (`gameInstanceType == "server"` — 2 in the schema).
- BepInEx-on-native-Linux-builds via doorstop scripts (see §7 — deploy works, auto-load
  doesn't; a follow-up slice).
- Cross-source mod identity/update checking (unchanged from DESIGN-modsources.md §9.3/§15).
- Minecraft/Modrinth (directive §7 phases 3–4).

## 2. What the research established (condensed)

### 2.1 The RoR2 module is already a generic engine wearing a game costume
Per-file audit of `src/NexusMods.Games.RiskOfRain2/`:
- **Game-specific:** exactly 6 constants in `RiskOfRain2Game.cs` (GameId string, display name,
  NexusModsGameId, Steam app id, exe name, art resources) + 2 stray literals (the Thunderstore
  community link in `MissingBepInExEmitter.cs:17`, the `NexusMods.RiskOfRain2.*` MnemonicDB
  attribute namespaces in the two loadout-item models).
- **Already generic:** both installers (`BepInExPackInstaller` keys on the shallowest
  `winhttp.dll` with a sibling `BepInEx/` — works for every `*-BepInExPack*` variant
  regardless of its root folder name; `BepInExPluginInstaller` implements exactly the
  canonical r2modman routing), the missing-loader emitter logic, `InstallerHelpers`, and the
  whole `user.reg` WINEDLLOVERRIDES patch in `RunRiskOfRain2Game` (only the class name and
  `RunGameTool<T>` type argument are RoR2-flavored).

### 2.2 N games from one class is feasible — with two known edges
- `IGameData<TSelf>`'s static-abstract members are a **convenience convention, not a runtime
  requirement**: nothing consumes them generically (no `TGame.GameId`/`typeof(TGame)` sites on
  game types). All consumers (SteamLocator, GameRegistry, `Loadout.ReadOnly.Game`,
  DiagnosticManager) read the **instance** `IGameData` from DI. A family game class implements
  plain `IGame` and skips `IGameData<TSelf>`.
- **Edge 1 — DI identity:** `AddGame<T>`/`AddAllSingleton` register one singleton **per type**
  (`src/NexusMods.Sdk/Extensions/ServiceCollection.cs:49`). The family bypasses `AddGame<T>`
  and registers each constructed instance explicitly in a `foreach` over the data table
  (`AddSingleton<IGame>(instance)` + `AddSingleton<IGameData>(instance)`, same object).
- **Edge 2 — unique DisplayName:** the PR D zero-sentinel path resolves Nexus-less games by
  display name (`Loadout.cs:86`), so display names must be unique across all Nexus-less games.
  Enforced at table-parse time. (Only H3VR and RoR2 are Nexus-less in the starter set.)
- Per-game tools: `RunGameTool<T>` injects an instance; mint one tool object per game instance
  (shared class, different injected game). `ToolManager` maps `tool.GameIds → tool` — fine.
- `GameId.From(string)` is a runtime FNV string pool — ids can be built from data at startup.
- `SteamLocator` builds a `SteamAppId → game` **FrozenDictionary that throws on duplicates**
  (`SteamLocator.cs:34`) — the family table must not repeat an app id already claimed by a
  hand-written module (this forces the RoR2 exclusion until its migration, §9).
- In-tree precedent: **CreationEngine** is the real family pattern (shared base + N thin
  subclasses + the config-as-data `StopPatternInstaller`). We go one step further: rows, not
  subclasses.

### 2.3 The ecosystem schema (live-verified 2026-07-08; snapshot analyzed in full)
- `https://thunderstore.io/api/experimental/schema/dev/latest/` — `schemaVersion 0.3.0`,
  1.35 MB, 296 games / 268 r2modman instances / 88 `modloaderPackages`. A machine-readable
  **JSON-Schema** for it is published at `…/schema/ecosystem-json-schema/latest/` (enum
  sources for codegen + validation).
- Per instance it carries everything an `IGame` needs: `meta.displayName`, `meta.iconUrl`
  (360×480 cover, base `https://gcdn.thunderstore.io/assets/`), per-instance `distributions`
  (Steam/EGS/Xbox/… ids), `steamFolderName`, `exeNames[]`, `dataFolderName`,
  `settingsIdentifier` (**unique across all 268 — our stable key**), `packageIndex` (encodes
  the community slug for ALL games — 46 legacy games incl. riskofrain2/valheim/subnautica have
  no `thunderstore` block, so slug must be derived from `packageIndex`, gotcha #1),
  `packageLoader`, `installRules[]`, `relativeFileExclusions`.
- **Homogeneity result: 204/214 BepInEx instances have byte-identical `installRules`** — the
  canonical 5 (plugins subdir `.dll` default / core subdir / patchers subdir / monomod subdir
  `.mm.dll` / config none) — exactly what `BepInExPluginInstaller` already hardcodes. The 10
  exceptions are additive routes (valheim `SlimVML`, gtfo `GameData`+`Assets`, h3vr
  `Sideloader`, ultrakill `UMM Mods`, timberborn `Maps`, nasb custom dirs) or tracking-method
  swaps (subnautica ×2: plugins/patchers/monomod as `state` + a `QMods` state route; patapon
  drops monomod).
- `trackingMethod` semantics (from r2modman's implementation): `subdir` = install into
  `route/{Namespace-Name}/` (what we do today); `none` = shared untracked dump (config);
  `state` = loose files into `route/`, ownership via r2modman state-file; `subdir-no-flatten` =
  subdir preserving archive structure; `package-zip` = drop the zip itself (godotml only).
  **Our loadout model makes `state` trivial:** the loadout already tracks per-file ownership
  natively, so `state` simply means "deploy loose into `route/` without a per-package
  subfolder" — no state files needed. This is an architectural win over r2modman, same class
  as the no-profile-directories win from DESIGN-modsources.md §8.
- No stable/versioned publication exists (`dev/latest` is the only channel; no releases/tags/
  npm). r2modmanPlus vendors a snapshot + validates remote refreshes against the JSON-Schema
  with AJV, merging remote-over-bundle at runtime with If-Modified-Since. Weekly-ish churn.
- The schema has **zero Linux/Proton knowledge**. r2modman keeps Proton decisions in sidecar
  depot files keyed on `settingsIdentifier`. The one Linux signal available: `exeNames`
  containing `.x86`/`.x86_64` marks the 15 games with native Linux builds (valheim, muck, …).
- `modloaderPackages[].rootFolder` names the folder-to-hoist inside loader zips (usually
  `BepInExPack`, sometimes `BepInExPack_Valheim`, sometimes empty = zip root). Our
  winhttp.dll-keyed detection already derives the hoist point structurally, so this field is
  informational for us, not load-bearing.

### 2.4 Cross-source reality of the family (games.json checked literally)
Most BepInEx games are **dual-source**: valheim (3667), lethalcompany (5848), subnautica
(1155), subnauticabelowzero (2706), gtfo (3657), ultrakill (3515), timberborn (4074),
contentwarning (6301), repo (7398), peak (7867), muck (5362) all have Nexus domains. h3vr and
riskofrain2 do not. Per DESIGN-modsources.md §15 rule 1, dual-source family games **must carry
their real `NexusModsGameId`** so nxm:// keeps working beside Thunderstore in one loadout —
only truly-exclusive games use the PR D zero sentinel. The schema has no Nexus ids, so the
mapping is a hand-curated field in our table (games.json is the lookup source).

## 3. Design overview

```
src/NexusMods.Games.BepInEx/                     (new project — the family engine)
  Schema/
    EcosystemSchemaParser.cs                     parse + validate vendored snapshot
    Assets/ecosystem-schema.json                 vendored snapshot (~1.35 MB, embedded)
    Assets/ecosystem-json-schema.json            published JSON-Schema (validation + enums)
  BepInExGameData.cs                             one row per supported game (curated table)
  Assets/bepinex-games.json                      the curated table: settingsIdentifier +
                                                 NexusModsGameId? + overrides (art, notes)
  GenericBepInExGame.cs                          : IGame (instance-only; NOT IGameData<TSelf>)
  RunBepInExGameTool.cs                          : RunGameTool<GenericBepInExGame> — the
                                                 user.reg winhttp patch, moved from RoR2
  Installers/BepInExPackInstaller.cs             moved from RoR2 project (unchanged logic)
  Installers/BepInExPluginInstaller.cs           moved + generalized: interprets installRules
  Emitters/MissingBepInExEmitter.cs              moved + parameterized community link
  Models/BepInExLoadoutItem.cs                   family markers, namespace NexusMods.BepInEx.*
  Models/BepInExPluginLoadoutItem.cs             (RoR2-namespace compat: §9)
  Resources/{SettingsIdentifier}.thumbnail.webp  vendored art per curated game
  Resources/{SettingsIdentifier}.tile.webp       (CreationEngine naming pattern)
  Services.cs                                    AddBepInExGames(): parse table → foreach row →
                                                 construct instance → explicit DI registration
```

Startup flow: `AddBepInExGames()` parses the embedded snapshot once, joins it against the
curated table (`bepinex-games.json`), constructs one `GenericBepInExGame` per row (client
instances only, `packageLoader == "bepinex"`), registers each as `IGame`+`IGameData` +
one `RunBepInExGameTool`. Everything downstream (SteamLocator, GameRegistry, loadouts,
diagnostics, Library/Downloads UI) sees ordinary `IGame` instances and needs **zero changes**.

**All BepInEx+Steam games register (decision §14.1 — Brian, 2026-07-08):** every schema client
instance with `packageLoader == "bepinex"` and a Steam id (~200 games) becomes a family game
in one stroke. The curated table below survives as the **verification set** — the games that
get an explicitly reviewed Nexus-id mapping, per-game QA attention, and (for Subnautica) the
end-to-end pilot. The rest ride the generated mapping (§4b) and shared placeholder art until
the art pipeline lands (§10):

| schema slug | settingsIdentifier | Steam | NexusModsGameId | why in the set |
| --- | --- | --- | --- | --- |
| subnautica | Subnautica | 264710 | 1155 | **pilot** — installed on dev box; dual-source; `state` rules + QMods |
| subnautica-below-zero | SubnauticaBZ | 848450 | 2706 | sibling; same rule shape |
| lethal-company | LethalCompany | 1966720 | 5848 | canonical rules; huge community |
| valheim | Valheim | 892970 | 3667 | +SlimVML route; native-Linux-build representative |
| content-warning | ContentWarning | 2881650 | 6301 | canonical; popular |
| repo | REPO | 3241660 | 7398 | canonical; popular |
| peak | PEAK | 3527290 | 7867 | canonical; popular |
| gtfo | GTFO | 493520 | 3657 | +GameData (subdir) + Assets (`state`) routes |
| ultrakill | ULTRAKILL | 1229490 | 3515 | +`UMM Mods` route |
| timberborn | Timberborn | 1062090 | 4074 | +Maps route |
| h3vr | H3VR | 450540 | — (Nexus-less) | +Sideloader `state`; exercises zero-sentinel path |
| riskofrain2 | RiskOfRain2 | 632360 | — (Nexus-less) | **migrates in PR G** — excluded from the table until then (duplicate-app-id guard, §2.2) |

`GameId.From(settingsIdentifier)` is the family's GameId scheme — and RoR2's existing
hand-written id is already `GameId.From("RiskOfRain2")` == its settingsIdentifier, so the PR G
migration is identity-preserving for free.

## 4. Schema vendoring and refresh

Consistent with the fork's cut-the-umbilicals stance (KIRO-HANDOFF §10) and r2modman's own
pattern:
- **Vendor the snapshot** (`Assets/ecosystem-schema.json`) + the published JSON-Schema,
  embedded resources. Parse at startup (fail-fast in Debug, log-and-skip-family in Release if
  the snapshot is corrupt).
- A `pull-ecosystem-schema` script/CLI verb re-fetches `dev/latest`, validates against the
  JSON-Schema, and rewrites the vendored file — a deliberate, reviewed re-vendor, same
  workflow as games.json.
- **No runtime fetching in this phase** (decision §14.2): the schema only matters for games in
  the curated table, which changes at our release cadence anyway. r2modman-style runtime
  merge-with-validation is the documented upgrade path when scale-out (PR H) makes it
  worthwhile — the parser is written against the validated shape either way. Parallels
  `FileHashesServiceSettings.EnableRemoteUpdates=false`.

Parsing notes (from gotchas): read distributions **per instance** (top-level is empty for
137 games); derive community slug from `packageIndex` (`/c/{slug}/`); `settingsIdentifier` is
the unique key; `identifier` nullable on `platform:"other"`; never construct slugs from names;
honor `gameSelectionDisplayMode == "hidden"`; skip instances whose Steam app id collides with
a hand-written module (RoR2's 632360 until PR G) or another instance (first wins, log).

### 4b. The generated Nexus-id mapping (new, forced by all-211)

At ~200 games the Nexus-id field can't be hand-curated. A build-side script/CLI verb
(`generate-nexus-mapping`) name-matches schema `meta.displayName` against the vendored
games.json (4,879 domains) — normalized compare (lowercase, strip non-alphanumerics), with
`additionalSearchStrings` as secondary keys. Output: `Assets/bepinex-nexus-ids.json`
(`settingsIdentifier → NexusModsGameId`), with ambiguous/no-match entries listed in a review
block. The 11 verification-set games' mappings are hand-confirmed (they already are, §2.4);
the long tail ships as generated. Unmatched games are Nexus-less (zero sentinel).

**Known caveat:** a game managed while unmapped writes zero-sentinel `GameInstallMetadata`;
adding its Nexus id later switches `TryGetMetadata` to id-lookup and orphans the old record
(the ghost-metadata class of problem, KIRO-HANDOFF §20.6). Mitigation: ship the mapping as
complete as possible in PR E, and treat mapping additions as needing a small
metadata-migration helper (or the eventual §9.4 rekey). Not a blocker — the sentinel path is
already the RoR2 steady state.

## 5. `GenericBepInExGame` (the one class)

Implements `IGame` with instance members fed from its `BepInExGameData` row:
- `GameId` = `GameId.From(row.SettingsIdentifier)`; `DisplayName` = schema `meta.displayName`;
  `NexusModsGameId` = row override (Optional — None for h3vr/ror2).
- `StoreIdentifiers` = per-instance distributions (Steam now; EGS/Xbox ids carried but only
  Steam is exercised — matches RoR2 precedent).
- `GetPrimaryFile` = first `.exe` in `exeNames` (Windows binary; the game runs under Proton —
  for native-Linux-build games see §7).
- `GetLocations` = `{ LocationId.Game → store path }` (single location, like RoR2).
- `Synchronizer` = `DefaultSynchronizer` (a shared `BepInExSynchronizer` with data-driven
  backup-ignore globs — e.g. `{dataFolderName}/StreamingAssets` — is a known refinement,
  deferred; the recognition pipeline + smallest-first backup cap already de-fang the backup
  problem for dual-source games).
- `LibraryItemInstallers` = `[BepInExPackInstaller, BepInExPluginInstaller(row.InstallRules)]`.
- `DiagnosticEmitters` = `[MissingBepInExEmitter(row.CommunitySlug)]` — link becomes
  `https://thunderstore.io/c/{slug}/p/bbepis/BepInExPack/`-class URL from data (exact loader
  pack per game can't be derived from the schema — the emitter links the community; the
  actual loader arrives through the dependency closure exactly as it did for RoR2).
- `IconImage`/`TileImage` = vendored embedded webp per row (decision §14.3).

## 6. Installers — `installRules` interpretation

`BepInExPluginInstaller` gains a rule table (parsed once per game from the schema) replacing
the hardcoded category map. Rule application, per archive path:
1. Strip `relativeFileExclusions` + the existing metadata skip-list (manifest.json, icon.png,
   README, LICENCE — the 9 games that set exclusions just confirm our current behavior).
2. Explicit-prefix match: a path starting with a rule's `route` (e.g. `BepInEx/plugins/…`,
   `QMods/…`) routes there (normalizing the `BepInEx/` prefix as today).
3. Extension match: `defaultFileExtensions` routes by extension (`.mm.dll` → monomod before
   `.dll` → plugins; longest-extension-wins).
4. Everything else → the `isDefaultLocation` rule.
5. Placement by `trackingMethod`: `subdir` → `route/{Namespace-Name}/…` (flattened, as today);
   `subdir-no-flatten` → `route/{Namespace-Name}/…` preserving archive structure; `state` and
   `none` → loose into `route/` (loadout tracks ownership; no state files — §2.3). Wrapping
   single folders stripped first (today's behavior).
6. `package-zip` and `subRoutes` are out of scope (no BepInEx game uses them) — the parser
   rejects rules it can't honor so a schema change can't silently misroute (fail the game row,
   not the app).

`BepInExPackInstaller` is moved verbatim — winhttp detection already generalizes over all
loader-pack variants (§2.3). Canonical-5 rules serve as the built-in fallback if a row has
empty rules (defensive only; all curated games have rules).

## 7. Launch — Proton override, and the native-Linux caveat

`RunBepInExGameTool` (the RoR2 user.reg patch, verbatim): before launch, ensure
`"winhttp"="native,builtin"` in the prefix's `[Software\\Wine\\DllOverrides]`; launch via
`steam://run/{appid}`. Prefix discovery via `LinuxCompatabilityDataProvider` — **no-ops
gracefully when no prefix exists**, so registering the tool for every family game is safe.

**Native-Linux builds (valheim, muck — 15 schema games):** when Steam uses the native build
there is no Proton prefix; mods deploy fine but BepInEx won't auto-load without the doorstop
`run_bepinex.sh` + launch-options plumbing. This phase: deploy works, the tool's override
no-ops, and we document "force Proton in Steam properties, or set launch options manually" —
r2modman has the same fundamental split (its Proton knowledge lives in hand-made depot
sidecars). A `bepinex-doorstop-linux` follow-up slice owns the native path (detect installed
binary is ELF via `exeNames`/`GetPrimaryFile` probe, write doorstop config, guide launch
options). Decision §14.4 covers whether Valheim ships in the starter set anyway.

## 8. Cross-source behavior (dual-source games)

Dual-source rows carry their real `NexusModsGameId` (§2.4), so: nxm:// downloads, the Nexus
CTAs, collections, and **local version recognition** (keyed on Nexus id — KIRO-HANDOFF §20.2)
all work unchanged, while Thunderstore one-click and the BepInEx installers work beside them
in the same loadout. This makes Subnautica the perfect pilot: Nexus-heavy modding history +
live Thunderstore community + installed on the dev box. H3VR/RoR2 exercise the Nexus-less
sentinel path (recognizer declines them; both are <5GB so the backup fuse is a non-issue).

## 9. Models + RoR2 migration (PR G)

The `NexusMods.RiskOfRain2.*` attribute namespaces are persisted in MnemonicDB (Brian's live
RoR2 loadout has them). Plan:
- Family models use fresh `NexusMods.BepInEx.*` namespaces.
- PR G deletes the RoR2 *project* but keeps its two model classes (relocated, `[Obsolete]`,
  original attribute strings) so existing datoms stay resolvable; `MissingBepInExEmitter`
  queries the union of old+new markers. New installs write family markers only.
- RoR2 becomes a table row (`RiskOfRain2`, GameId-identical — §3), its art moves to the family
  `Resources/`, `RunRiskOfRain2Game`/installers/emitter/Services are deleted in favor of the
  family versions. Loadout continuity: GameId string unchanged ⇒ `GameInstallMetadata`
  (Path+Store matched) and loadout `Game` resolution (DisplayName unchanged) both hold.

## 10. Art (decision §14.3: runtime fetch + cache)

A caching HTTP `IStreamFactory`: resolves `https://gcdn.thunderstore.io/assets/{iconUrl}`
(360×480 covers → tile 140×210, same portrait orientation; community 192×192 icon for the
thumbnail where present), caches to disk under the app's data dir, serves the cached file on
subsequent reads, and falls back to the shared placeholder webp (RoR2-style) when offline or
uncached. This is its own PR (§13 PR H') because it's reusable infrastructure — the same
pipeline is the natural carrier for Brian's §20.7 backlog item (Thunderstore *mod* icons in
the Library). Until it lands, every family game shows the shared placeholder, exactly as RoR2
did in Phase 1.

## 11. Gating and release

Family games are registered always but visible only via `EnableAllGames` (debug) — same as
RoR2 today. Graduating a game to release = adding its GameId to
`ExperimentalSettings.SupportedGames` after a QA pass. No new settings surface this phase
(`ThunderstoreSettings.EnableThunderstore` continues to gate the one-click/source side).

## 12. Testing

- **Schema parser:** snapshot round-trip (296 games parse; enum coverage; the 46
  legacy-community games get slugs from packageIndex; nullable identifiers; reject-unknown
  trackingMethod behavior).
- **Rule engine:** one test per trackingMethod semantic + the real deviant rule sets
  (subnautica QMods/state, gtfo Assets, valheim SlimVML, ultrakill UMM Mods) on the
  synthetic-zip harness (`ALibraryArchiveInstallerTests` + `UniversalStubbedGameLocator`) —
  the RoR2 installer tests (5) move over and must stay green under canonical rules.
- **Registration:** N distinct instances resolve from DI; SteamLocator dictionary builds with
  no duplicate app ids; unique-DisplayName guard trips on a synthetic dup; zero-sentinel vs
  real-Nexus-id rows take the right GameInstallMetadata path.
- **Live (Brian, GUI):** manage Subnautica → nxm download from Nexus + one-click from
  Thunderstore into the same loadout → Apply → launch under Proton with BepInEx loading.
  RoR2 regression after PR G migration.

## 13. Deliverable slicing (each its own PR into `linux-fork`)

- **PR E — family core, all games:** new project, vendored schema + parser, generated
  Nexus-id mapping (§4b) with the 11 verification games hand-confirmed, `GenericBepInExGame`
  ×~200 + registration loop + `RunBepInExGameTool`, installers *moved* from RoR2 (canonical
  behavior preserved; RoR2 project temporarily references the family project for its
  installers), shared placeholder art, registration/parser/mapping tests. Games appear in
  debug builds; canonical-rule games fully functional end-to-end.
- **PR F — schema-driven rules:** the §6 rule engine in `BepInExPluginInstaller`; deviant-rule
  games (Subnautica!) become correct; rule-engine test suite. **Pilot verification: Subnautica
  end-to-end on the dev box.**
- **PR G — RoR2 folds in:** delete the RoR2 project per §9 (model-compat preserved), RoR2 row
  enabled in the table, regression pass on Brian's live loadout.
- **PR H' — runtime art pipeline:** the §10 caching stream factory for game tiles/icons;
  groundwork for Thunderstore mod icons (§20.7 backlog).
- **Later slices (unscheduled):** native-Linux doorstop support; runtime schema refresh;
  non-BepInEx loaders.

## 14. Decisions (Brian, 2026-07-08 design review)

1. **Game set: ALL ~200 BepInEx+Steam games in PR E** (not the curated-only option). The
   11-game table remains the verification set; the long tail rides the generated Nexus-id
   mapping and placeholder art.
2. **Schema refresh: vendor-only** with a deliberate re-vendor verb; runtime merge deferred.
3. **Art: runtime fetch + cache** (its own PR H'); placeholder until then.
4. **Valheim: in**, with documented force-Proton guidance; native-build doorstop is a later
   slice.
