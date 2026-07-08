# DESIGN — Mod-source abstraction, Phase 1: Thunderstore as a peer source

> Written 2026-07-08. Implements directive §7 Phase 1 (KIRO-HANDOFF.md): generalize "a mod
> source" so Thunderstore sits beside the Nexus API as a peer — metadata, versioning,
> dependency resolution, download, and `ror2mm://` one-click. Design reviewed before code.
> All Thunderstore API claims below were live-verified on 2026-07-08.

---

## 1. Goal and non-goals

**Goal (Phase 1):** a user clicks "Install with Mod Manager" on thunderstore.io and the app
downloads the package *and its full dependency chain* into the Library, from which it can be
installed into a loadout of a supported game — with the same jobs/progress, the same Library
UI, and the same undo model as a Nexus mod. Plus CLI verbs to drive the same pipeline
headlessly.

**Non-goals (Phase 1)** — each deliberately deferred, not forgotten:
- **No in-app browse/search page.** The app has *no* catalog UI for Nexus either — discovery
  happens on the website, which emits `nxm://` links. Thunderstore's model is identical
  (website emits `ror2mm://`). We inherit that symmetry for free and keep UI scope near zero.
- **No generic BepInEx game family** (that's Phase 2, §8) — Phase 1 ships ONE pilot game
  module wired by hand to prove install-into-loadout.
- **No bulk package-index mirroring.** Metadata is fetched per-package on demand
  (experimental API). r2modman mirrors the whole index because it has in-app browse; we
  don't. Update checking (which would want the index) is Phase 1.5.
- **No update checking for Thunderstore items yet.** `IModUpdateService` is Nexus-typed;
  generalizing it is its own change (§9.3).
- **No collections/modpack integration, no launch-argument (doorstop) handling, no Minecraft.**

**License note (corrects the task brief):** r2modmanPlus is **MIT** (verified from its LICENSE,
not GPL-3 as previously assumed). MIT is GPL-compatible, so both code and format knowledge can
still flow into this GPL-3 fork — attribution only.

---

## 2. What the research established (condensed)

### 2.1 The core is already source-agnostic — the seams exist
- `ILibraryService.AddDownload(IJobTask<IDownloadJob, AbsolutePath>)` accepts ANY download
  job (`src/NexusMods.Abstractions.Library/ILibraryService.cs:24`).
- `AddDownloadJob.StartAsync` → `DownloadJob.JobDefinition.AddMetadata(tx, libraryFile)`
  (`src/NexusMods.Library/AddDownloadJob.cs:40`) is the **single choke point** where a source
  stamps its identity onto a generic `LibraryFile`. `ExternalDownloadJob` (collections'
  direct-download path) already proves a non-Nexus source works end-to-end through it.
- `HttpDownloadJob` (`src/NexusMods.Networking.HttpDownloader/HttpDownloadJob.cs`) is a
  generic resumable HTTP downloader with a `virtual AddMetadata` — subclass and go.
- Installers (`ILibraryItemInstaller` chain) and `ILoadoutManager.InstallItem` are fully
  source-agnostic; a game's installers + the AdvancedManual fallback apply to any library item.
- The Library page aggregates `GetServices<ILibraryDataProvider>()` — a new source is one
  more registration (`src/NexusMods.App.UI/Services.cs:304-309`).
- Protocol dispatch is scheme-keyed over all `IIpcProtocolHandler`s
  (`CommandLineConfigurator.RunLink`, `ProtocolVerbs.protocol-invoke`) — a `ror2mm` handler
  slots in without touching dispatch.
- `IGameData.NexusModsGameId` is `Optional` — a Thunderstore-only game (Lethal Company) is
  already representable; identity is our own `GameId` (FNV-1a of a name).

### 2.2 Where Nexus is privileged (what "peer" has to mean)
- `NxmIpcProtocolHandler` is the only handler and is a monolith (OAuth + GOG auth + mods +
  collections in one switch). We add a sibling, we don't touch it.
- `HandlerRegistration` (`Networking.NexusWebApi/HandlerRegistration.cs:24`) hardcodes
  registering scheme `"nxm"`; the `.desktop` file hardcodes `MimeType=x-scheme-handler/nxm`.
- `DownloadsService` (`src/NexusMods.Library/DownloadsService.cs:50`) — the Downloads page —
  subscribes ONLY to `INexusModsDownloadJob` and types `DownloadInfo.GameId` as
  `NexusModsGameId`. **This is the one genuinely non-additive refactor Phase 1 needs** (§9.1).
- `LibraryItemRemovalInfo` (`App.UI/Pages/Library/LibraryItemRemovalInfo.cs:25`) equates
  "redownloadable" with "is a Nexus item" — drives the delete-warning UX (§9.2).
- `GameRegistry.TryGetMetadata` keys on `NexusModsGameId` and no-ops without one (in-code
  `TODO: use game id instead` at `GameRegistry.cs:109`) (§9.4).
- Update service, V1/V2 game-identity mapping, collections orchestration: Nexus-only, all
  out of Phase 1 scope.

### 2.3 Thunderstore facts (live-verified 2026-07-08)
- **Identity:** `namespace` + `name` (globally unique pair) + semver `version_number`;
  `full_name` = `Namespace-Name[-1.2.3]`. Dependencies are arrays of **exact-version strings**
  `"Namespace-Name-1.2.3"`.
- **Per-package metadata (our Phase 1 workhorse):**
  `GET /api/experimental/package/{ns}/{name}/` (includes `latest` + community listings) and
  `GET /api/experimental/package/{ns}/{name}/{version}/`. Anonymous, CORS-open, no published
  rate limits. Changelog/readme endpoints exist (`{"markdown": ...}`).
- **Download:** `GET https://thunderstore.io/package/download/{ns}/{name}/{version}/` →
  302 to a CDN zip (`gcdn.thunderstore.io`). Package zip = `manifest.json` + `icon.png`
  (256×256) + `README.md` [+ `CHANGELOG.md`] + payload.
- **One-click:** `ror2mm://v1/install/thunderstore.io/{ns}/{name}/{version}/` (r2modman also
  accepts `{sub}.thunderstore.io` hosts). No `thunderstore://` scheme exists.
- **Communities** (game slugs, e.g. `lethal-company`): `GET /api/experimental/community/`
  (cursor-paginated; 284 today via the newer endpoints).
- **Ecosystem schema (the big find):** `GET /api/experimental/schema/dev/latest/` — 296 games,
  250 communities, 88 `modloaderPackages`, per-game `installRules`, `packageIndex` URLs,
  Steam/EGS distribution ids. **r2modman no longer hardcodes games; it consumes this schema.**
  Phase 2's generic BepInEx family should be schema-driven. Consistent with the fork's
  umbilical policy: vendor a snapshot, refresh optionally at runtime (schema is `dev/latest`
  v0.3.0 — treat as unstable, always keep the vendored fallback).
- Bulk indexes exist when we need them later (per-community chunked
  `package-listing-index` on CDN blobs; ecosystem-wide NDJSON `package-index`, 78 MB gz).

---

## 3. Design overview

**Principle: a "mod source" is a set of registrations against existing seams, not a god
interface.** Nexus already works this way implicitly (download job + entities + data provider
+ protocol handler + DI module). We make the pattern explicit and repeatable, and only
introduce new abstractions where a cross-cutting consumer genuinely needs to enumerate
sources (§9). We do NOT build an `IModSource` mega-interface that forces Nexus's OAuth or
collections into a generic shape they don't fit.

New projects (mirroring the Nexus split so UI/dep layering works):

| Project | Contents |
| --- | --- |
| `src/NexusMods.Abstractions.Thunderstore/` | MnemonicDB entities, `ThunderstoreUrl` parser, `IThunderstoreLibrary`, DTOs |
| `src/NexusMods.Networking.Thunderstore/` | API client, `ThunderstoreLibrary`, `ThunderstoreDownloadJob`, dependency resolver, CLI verbs |
| `tests/Networking/NexusMods.Networking.Thunderstore.Tests/` | unit tests (parser, resolver, client against canned JSON) |

The `ror2mm` IPC handler lives in `NexusMods.App.Cli` beside `NxmIpcProtocolHandler` (same
pattern, same DI file). The library data provider lives in `NexusMods.App.UI` beside
`NexusModsDataProvider` (App.UI references `Abstractions.Thunderstore`).

Feature-gated: everything registers only when `ThunderstoreSettings.Enabled` (new
`ISettings` record, `Section = Sections.Experimental`, `RequiresRestart = true`). Default ON
in debug, OFF in release until it has soaked.

---

## 4. Data model (MnemonicDB entities)

Mirror the proven Nexus triple — Package ≙ ModPage, Version ≙ FileMetadata:

```
ThunderstorePackageMetadata            (≙ NexusModsModPageMetadata)
  Namespace     : String   (indexed)
  Name          : String   (indexed)
  FullName      : String   (indexed, "Namespace-Name" — the natural lookup key)
  PackageUri    : Uri      (thunderstore.io page, community-qualified when known)
  IconUri       : Uri      (optional)
  Versions      : BackReference<ThunderstoreVersionMetadata>

ThunderstoreVersionMetadata            (≙ NexusModsFileMetadata)
  Package       : Reference<ThunderstorePackageMetadata>
  VersionNumber : String   ("1.2.3")
  FullName      : String   (indexed, "Namespace-Name-1.2.3")
  Dependencies  : Strings  (the exact-version dependency strings, as published)
  FileSize      : Size     (optional)
  UploadedAt    : Timestamp(optional)

ThunderstoreLibraryItem [Include<LibraryItem>]   (≙ NexusModsLibraryItem)
  Version       : Reference<ThunderstoreVersionMetadata>
```

Notes:
- Registered via `AddThunderstoreModels()` in `Abstractions.Thunderstore/Services.cs`
  (mirror `AddNexusModsLibraryModels()`).
- `GetOrAdd` semantics identical to `NexusModsLibrary.GetOrAddModPage/GetOrAddFile`: look up
  by indexed `FullName`, create on miss, single transaction per download.
- Dependency strings are stored verbatim on the version (they're immutable facts about a
  published version) — the resolver reads them without a second API call for anything
  already in the DB.
- We deliberately do NOT store a community/game reference on the package: Thunderstore
  packages are global and multi-community; the *target game* is a property of the install
  action (which loadout), not of the package. This avoids inventing a wrong constraint the
  upstream data model doesn't have.

---

## 5. Download pipeline

Mirror `NexusModsLibrary` + `NexusModsDownloadJob` exactly:

1. **`IThunderstoreLibrary` / `ThunderstoreLibrary`** —
   `CreateDownloadJob(destination, ns, name, version, ct)`:
   - `GetOrAddPackage` / `GetOrAddVersion` (experimental API → entities).
   - Builds a `ThunderstoreDownloadJob : HttpDownloadJob` (the `ExternalDownloadJob`
     subclass pattern, not the Nexus wrapper pattern — simpler, and pause/resume works
     natively since our job IS the HTTP job) with
     `Uri = thunderstore.io/package/download/{ns}/{name}/{version}/` (the 302 to the CDN is
     followed by the HTTP stack; `HttpDownloadJob` already handles range-resume).
   - Its `AddMetadata(tx, libraryFile)` override sets `LibraryItem.Name` =
     `"{Name} ({Namespace})"` + `FileName` = `{FullName}.zip`, stamps
     `ThunderstoreLibraryItem.VersionId`, and creates the generic `DownloadedFile` with
     `DownloadPageUri` (same shape as `NexusModsDownloadJob.AddMetadata`, `:100`).
2. Caller passes the job to `ILibraryService.AddDownload(...)` — everything downstream
   (hashing, archive analysis, `LibraryArchive` tree, `IFileStore` backup) is inherited.
3. **Idempotency:** before downloading, check for an existing `ThunderstoreLibraryItem`
   whose version `FullName` matches (mirror `NexusModsLibrary.IsAlreadyDownloaded`); the
   one-click handler and the resolver both consult this so re-clicks and shared dependencies
   are no-ops.

## 6. Dependency resolution

Thunderstore dependencies are exact-version strings, so this is NOT constraint solving:

- `ThunderstoreDependencyResolver.ResolveAsync(ns, name, version, ct)` →
  ordered `IReadOnlyList<PackageVersionRef>`:
  - BFS from the root; parse each dependency string (`Namespace-Name-1.2.3` — parse from the
    RIGHT, since namespace/name may themselves contain `-`; the version segment is the
    always-parseable semver tail).
  - **Same package requested at two versions → take the semver-max** (r2modman behavior;
    Thunderstore convention is "latest wins" within a profile).
  - Visited-set on `Namespace-Name` (post max-merge) prevents cycles; depth cap (say 64) as
    a tripwire against pathological data.
  - Every resolved node not already in the Library becomes its own download job — each is a
    separate Library item with real progress in the Downloads page, mirroring how r2modman
    materializes dependencies as individual mods.
- The **loader package** (`BepInExPack` et al.) will typically appear in every chain. Phase 1
  downloads it like any other item; *deploying* it correctly is the pilot game's loader
  installer's job (§8), same relationship as SMAPI-the-archive vs `SMAPIInstaller`.

## 7. `ror2mm://` one-click + scheme registration

- **`ThunderstoreUrl.Parse`** (in Abstractions.Thunderstore, mirroring `NXMUrl`):
  `ror2mm://v1/install/{host}/{ns}/{name}/{version}/` — accept `thunderstore.io` and
  `{sub}.thunderstore.io` hosts (r2modman's regex does); reject anything else loudly.
- **`Ror2mmIpcProtocolHandler : IIpcProtocolHandler`** (`Protocol => "ror2mm"`) in
  `NexusMods.App.Cli/Types/IpcHandlers/`, registered in `App.Cli/Services.cs` beside the nxm
  handler. Flow: parse → resolve dependency closure → enqueue download jobs via
  `ILibraryService.AddDownload` → notify (reuse the existing toast/notification pattern the
  nxm handler uses). No login gate — Thunderstore is anonymous.
- **Scheme registration:** generalize instead of duplicating the hardcoded pattern:
  - Move nothing heavy; add `UriSchemeRegistration : BackgroundService` (implemented in
    `NexusMods.App.Cli`, where `IIpcProtocolHandler` lives) that iterates
    `GetServices<IIpcProtocolHandler>()` and calls
    `IOSInterop.RegisterUriSchemeHandler(handler.Protocol)` for each. Retired
    `HandlerRegistration`'s hardcoded `"nxm"` call in favor of it. Handlers gained a
    default-true `IsEnabled` property so settings-gated handlers (ror2mm reads
    `ThunderstoreSettings`) are skipped by registration and no-op in `Handle`.
  - `.desktop` file: `MimeType=x-scheme-handler/nxm;x-scheme-handler/ror2mm;`
    (`src/NexusMods.App/com.nexusmods.app.desktop:11`).
  - Add CLI verb `associate-ror2mm` mirroring `associate-nxm`.
  - Windows/macOS interop is already scheme-parameterized; they just receive the extra call.

## 8. Pilot game module (proves install-into-loadout; Phase 2 makes it generic)

One new project `NexusMods.Games.<PilotGame>/` following CHECKLIST A from the research
(static-interface `IGame, IGameData<TSelf>`; `NexusModsGameId = Optional.None` if
Thunderstore-only; `StoreIdentifiers.SteamAppIds` so the Steam locator finds it):

- **`BepInExInstaller : ALibraryArchiveInstaller`** — detects the BepInExPack layout (root
  folder from the pack, e.g. `BepInExPack/`), deploys `BepInEx/core/…`, `winhttp.dll`,
  `doorstop_config.ini` into `LocationId.Game`. Modeled on `SMAPIInstaller`
  (loader-as-archive-installer precedent) — but simpler: no launcher swap on Linux-native
  Unity games; Proton games rely on Steam's winhttp override (documented, §10).
- **`BepInExPluginInstaller : ALibraryArchiveInstaller`** — routes package payload to
  `BepInEx/plugins/{Namespace-Name}/` (the ecosystem convention; `subdir` method), with the
  standard special dirs honored when present in the archive (`plugins/`, `patchers/`,
  `config/`). Phase 1 implements the common-case rules by hand; Phase 2 swaps in
  schema-driven `installRules`.
- **`MissingBepInExEmitter : ILoadoutDiagnosticEmitter`** — plugins present but no loader
  group in the loadout (mirror `MissingSMAPIEmitter`).
- Key deviation from r2modman: **no profile directories.** r2modman isolates via per-profile
  BepInEx trees + doorstop launch args; *our isolation is the loadout/synchronizer itself*
  (deploy/undeploy is already versioned and undoable). This is the fork's structural
  advantage — BepInEx games get git-like profiles for free. Launch-arg management
  (`--doorstop-*`) is therefore NOT needed for the deploy-into-game-dir model.
- Release gating: the pilot stays out of `ExperimentalSettings.SupportedGames` until proven
  (visible in debug/`EnableAllGames`).

**Pilot game: Risk of Rain 2** (decided by Brian, 2026-07-08 — owned on this box; the
original r2modman game; standard BepInExPack). Steam AppId 632360; Thunderstore community
`riskofrain2`; has a Nexus presence too (so §9.4 doesn't bite in Phase 1).

## 9. The cross-cutting generalizations (the only non-additive edits)

These four are the real "extract the seam" work; everything above is additive.

### 9.1 Downloads page: `DownloadsService` de-Nexusing (medium risk — behind tests)
Today it observes only `INexusModsDownloadJob` and `DownloadInfo.GameId : NexusModsGameId`.
Change:
- New small interface in `Abstractions.Downloads`:
  `ILibraryDownloadJob : IDownloadJob { string DisplayName { get; } GameId GameId { get; } IJobTask<IHttpDownloadJob, AbsolutePath> HttpDownloadJob { get; } }`
  — implemented by `NexusModsDownloadJob` (name/game already derivable from its
  `FileMetadata`) and `ThunderstoreDownloadJob` (game = the install target's `GameId`, or
  `GameId.DefaultValue` when library-only; the Downloads page already tolerates unresolved
  game names).
- `DownloadsService` subscribes `GetObservableChangeSet<ILibraryDownloadJob>()`;
  `DownloadInfo.GameId` becomes `GameId`; `IDownloadsService.GetDownloadsForGame(GameId)`.
  Follows the in-code `TODO: use game id instead of nexus mods game id`.
- UI: `IDownloadsDataProvider.ResolveGameName(GameId)` via `IGameRegistry` instead of the
  Nexus mapping cache.
- Risk containment: pause/resume unwrapping (`ResolveToHttpDownloadJobId`, issue #3892
  workaround) moves onto the interface, so behavior for Nexus downloads is unchanged; add a
  regression test that a Nexus download and a Thunderstore download both surface, pause, and
  resume.

### 9.2 "Redownloadable" as a capability, not `IsNexus`
`LibraryItemRemovalInfo.cs:25-28`: replace the `TryGetAsNexusModsLibraryItem` special case
with "is Nexus item OR is Thunderstore item" via a tiny helper
(`LibraryItemSourceInfo.IsRedownloadable(item)`) in one place. (A fuller
source-capability registry is YAGNI until source #3.)

### 9.3 Update checking — explicitly deferred
Phase 1 ships without it (parity loss vs r2modman, accepted). Phase 1.5 design sketch: a
`IThunderstoreUpdateChecker` querying `/api/experimental/package/{ns}/{name}/` per distinct
library package (N small anonymous GETs; fall back to the chunked community index if N grows),
surfaced through the same `ILibraryDataProvider` version-column components Nexus uses. Do NOT
generalize `IModUpdateService` yet — wait for this second concrete implementation to show the
right shape (same rule as the store-agnostic recognizer in FABLE5-TASKS §4).

### 9.4 `GameRegistry.TryGetMetadata` NexusModsGameId dependence
Blocks nothing in Phase 1 for a pilot game with a `NexusModsGameId` (Valheim/RoR2/Subnautica
all have Nexus presence too — even Lethal Company does). Fix the in-code TODO (key
`GameInstallMetadata` on `GameId`) as an independent small PR if the pilot turns out to hit
it; otherwise leave for Phase 2 where Thunderstore-only games are the point.

## 10. Proton note (for the record, affects Phase 2 not Phase 1)

For BepInEx-under-Proton games, r2modman sets `WINEDLLOVERRIDES="winhttp=n,b"` AND patches
`compatdata/{appid}/pfx/user.reg` DllOverrides. In our deploy-into-game-dir model the
override is still required for Wine to load the proxy `winhttp.dll`. The clean fork answer:
a diagnostic that tells the user to add the override to the game's Steam launch options
(Phase 1 pilot, if the pilot is played under Proton), and a `GameToolRunner`-style automated
`user.reg` patch as Phase 2 hardening (pairs naturally with the §3.1 protontricks→umu work).
Native-Linux Unity builds (Valheim has one) need doorstop's `LD_PRELOAD`-style wrapper only
when BepInEx is *outside* the game dir — deploying INTO the game dir with the pack's own
`run_bepinex.sh` avoids launch-arg plumbing for the pilot.

## 11. CLI surface (headless verification, mirrors the recognition work's pattern)

- `thunderstore download -p <Namespace-Name> [-v 1.2.3] [-n/--noDeps]` — resolve + download
  into the Library (prints the resolved closure and per-package results; skips packages
  already present). Named `download`, not `install` — it doesn't touch loadouts.
- `thunderstore resolve -p <Namespace-Name> [-v ...] [-n]` — print the dependency closure
  without downloading (pure, great for tests/debugging).
- Existing `loadout install` then covers Library → loadout headlessly.

## 12. Testing strategy

- **Unit (no network):** URL parser (valid/invalid/subdomain hosts); dependency-string
  parser (right-anchored version split, names containing `-`); resolver (diamond deps,
  version-max merge, cycle tripwire) against canned JSON fixtures; installer layout routing
  against synthetic `LibraryArchiveTree`s (pattern: existing `LocalFileHasherTests`).
- **Integration (network, opt-in like the real-data recognition validation):** resolve +
  download a tiny real package (e.g. a BepInEx config lib, a few KB) end-to-end into a
  test Library; assert entities + archive analysis.
- **Regression:** DownloadsService dual-source test (§9.1); Nexus flows untouched
  (existing suites stay green: synchronizer 12, DataModel 232, Steam 13+6).
- **Manual GUI:** one-click from thunderstore.io on the pilot game → Downloads page progress
  → Library shows items with icons/names → install into loadout → Apply → plugin present in
  `BepInEx/plugins/`.

## 13. Deliverable slicing (each its own PR into `linux-fork`)

1. **PR A — plumbing:** projects + entities + API client + `ThunderstoreLibrary` +
   `ThunderstoreDownloadJob` + resolver + CLI verbs + unit tests. Purely additive; app
   behavior unchanged unless the verbs are invoked.
2. **PR B — one-click:** URL parser + `Ror2mmIpcProtocolHandler` + generalized scheme
   registration + `.desktop` + settings gate + `associate-ror2mm`.
3. **PR C — downloads/library UI:** `DownloadsService` generalization (§9.1) +
   `ThunderstoreDataProvider` (Library page: name, icon, version column) + removal-info
   capability (§9.2) + localized strings.
4. **PR D — pilot game:** game module + BepInEx loader/plugin installers + missing-loader
   diagnostic + (if Proton) launch-options diagnostic.

Order matters: A is verifiable headless immediately; B/C/D each land on a proven layer.

## 14. Decisions (Brian, 2026-07-08 design review)

1. **Pilot game:** Risk of Rain 2 (owned; see §8).
2. **Settings default:** ON in debug, OFF in release via `ThunderstoreSettings`
   (Experimental section, RequiresRestart).
3. **Ecosystem schema vendoring:** deferred to Phase 2 — Phase 1 hardcodes only stable
   conventions (download URL shape, plugins dir).
4. **Naming:** keep the `NexusMods.*` project prefix for now (matches every other project;
   the fork-wide rename is a re-branding decision, not a Phase 1 one).
5. **Design approved; PR A (plumbing) green-lit** — purely additive, behind tests, on
   branch `feature/thunderstore-source`.
