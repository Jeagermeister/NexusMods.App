# Apocrypha — Complete Code Review & Analysis

*A full-repository audit of efficiency, optimization, mod-source extensibility, and mod
loading / load-order — commissioned 2026-07-13, against `linux-fork` @ `80037f4`.*

> **✅ Verification complete (2026-07-14).** The pass that was paused by a spend limit has been
> finished and the completeness critic has run. See
> [`docs/CODE_REVIEW_VERIFICATION.md`](./CODE_REVIEW_VERIFICATION.md) for the full record: all 37
> previously-unverified findings now have a verdict (**36 confirmed, 1 refuted**), plus **~33 new
> findings** the 14-dimension pass missed — 7 high, headlined by a **confirmed arbitrary-file-write
> in the FOMOD installer**. §8.1 below summarizes; the appendix table reflects the new verdicts.

---

## 1. How to read this document

This is a deep audit of the whole codebase (~130,000 lines of C# across ~1,656 files),
produced by fanning fourteen independent domain reviewers across the repo, then running an
adversarial verification pass that tried to *refute* each significant finding by re-reading
the actual source. The headline numbers:

| | |
|---|---|
| Reviewers (domains) | 14 (+ 7 completeness-critic sweeps) |
| Raw findings | 177 (+ ~33 from the completeness pass) |
| Adversarially verified | **139** (verification now complete) |
| **Confirmed real** | **99** (63 original + 36 from the finished pass) |
| Refuted on verification | **3** (2 original + `se3`) |
| **Critical (data-loss/crash by design)** | **0** (but see the FOMOD arbitrary-write, §8.1 — high, arguably critical) |
| New high-severity findings (completeness) | 7 |
| High (with verifier severity corrections applied) | ~34 |
| Medium | ~85 |
| Low / Info | ~78 |

Every finding cites `file:line` you can click straight to. Where a finding was
adversarially verified, its severity below already reflects the verifier's correction (a few
were downgraded — e.g. one "critical directory-delete" was proven unreachable and dropped to
low; two token/download races were narrowed to medium because the timing windows are
microseconds). **As of 2026-07-14 the verification pass is complete** — the 37 findings that
were still *unverified* when this was first published have all been through the refutation pass
(36 confirmed with severity corrections, 1 refuted: `se3`), and the completeness critic added
~33 new findings. Verdicts and the new findings are recorded in
[`CODE_REVIEW_VERIFICATION.md`](./CODE_REVIEW_VERIFICATION.md) and folded into the appendix below.

Sections 4–6 answer your three specific questions. Section 7 is the prioritized to-do list.
The full 175-row findings table is the appendix.

---

## 2. Overall verdict

**This is a healthy, unusually well-executed fork — well above typical fork quality — that
is one sprint of hardening away from being genuinely solid.** The code you and contributors
added (Thunderstore, the ~200-game BepInEx family, mod.io, the load-order rework, the
file-conflict UI, local Steam-manifest recognition) is thoughtfully engineered: intent is
documented inline, the hard algorithms have real tests that assert *behavior* (the
Thunderstore resolver even asserts API round-trip counts), and the risky rebrand was done
with genuine care — all 76 on-disk MnemonicDB namespace strings and every serialized page
identifier were deliberately left as `NexusMods.*` so existing databases survive the rename.
Telemetry removal is verifiably complete. There are **zero** critical findings, which is rare
for a codebase this size.

The problems cluster in five recurring themes, and they are fixable:

1. **A shared-singleton bug in load order** that three independent reviewers found from three
   different angles — `SortOrderManager` is a process-wide singleton whose per-game
   registration *replaces* rather than *merges* state, so opening a second game's page wipes
   Cyberpunk's REDmod load order for the session.
2. **State not carried across "copy" operations** — `CopyLoadout` silently drops the user's
   load order *and* all file-conflict priorities, so a cloned loadout can deploy different
   winning files than the original.
3. **The mod-source architecture is three parallel silos**, not one extensible seam. Adding
   the *4th* source (Modrinth is on your roadmap) means hand-editing ~10 seams across 6+
   projects — and the existing three are already inconsistent at several of them.
4. **A frozen-data strategic liability** — the game-hashes DB, `games.json`, and the BepInEx
   ecosystem schema are all static snapshots (hash DB dated 2025-09-30) with remote updates
   disabled, so every game patch since then funnels users into the "unknown version" paths.
5. **A test/CI coverage gap exactly on the fork's riskiest code** — the two anti-data-loss
   guards, the Proton `user.reg` writer, the modpack orchestrator, and the whole
   `RequiresNetworking` suite either have no tests or never run in CI.

None of these is architectural rot; each is a contained, well-understood fix. The fork
diverged cleanly from the archived upstream and is very much worth continuing to maintain.

### Subsystem scorecard

| Subsystem | Grade | One-line take |
|---|---|---|
| Loadout sync / 3-way merge | A− | Genuinely well-designed stateless merge, `O(changed files)` disk I/O; weak spots are Linux case-sensitivity and a few `O(n·m)` scans |
| Nexus source | B+ | Feature-rich, excellent feed-based update checking; error handling at the API boundary is the soft spot |
| Thunderstore source + BepInEx family | A− | Best-engineered fork code; adaptive dependency resolver, data-driven 200-game family |
| mod.io source | B | Clean Phase-1 port; a few DTO/robustness gaps and intentional MVP omissions |
| Load order / sorting | C+ | Good data model, but the singleton bug, nondeterministic ties, and an `O(N³)` plugin sort hold it back |
| Data layer (MnemonicDB) | B+ | Disciplined transactions; unbounded history growth and one broken migration are the concerns |
| Downloads / networking | B+ | Solid resumable engine and Steam recognition; one real resume-corruption bug |
| UI (Avalonia/R3) | B | Good lifecycle discipline; scale behaviors will bite past ~1,000 items |
| Async / concurrency | B | Newer code careful; the inherited job framework has real lifecycle races |
| Mod-source extensibility | C | The seam that matters most for your roadmap is the least abstracted |
| Tests / benchmarks | C+ | Strong infra & good unit coverage of pure logic; the effectors that mutate user data are untested |
| Security | B+ | The "obvious" mod-manager attacks are handled right; at-rest secrets and download integrity are the gaps |
| Startup / DI / process model | B | Solid DI; PID-based process arbitration and silent-failure paths are the risks |
| Fork health vs. upstream | A− | Clean divergence, identity preserved, telemetry gone; frozen data is the one strategic debt |

---

## 3. What the fork does exceptionally well

Worth stating plainly, because it's a lot:

- **The rebrand preserved on-disk identity.** R5 renamed 2,611 files `NexusMods.*` →
  `Apocrypha.*` but deliberately kept every `Namespace = "NexusMods.*"` model string and every
  `[JsonName("NexusMods.App.UI...")]` page id, so existing event-sourced databases and saved
  window layouts survive the rename. Only genuinely-new models (mod.io) use `Apocrypha.*`
  identity. This is the single most dangerous thing a fork can get wrong, and it's correct.
- **Telemetry removal is complete and verifiable.** Mixpanel/Matomo/OpenTelemetry gone
  (3,394 lines, 93 files), and even Matomo campaign parameters were stripped from outbound
  Nexus URLs (`NexusModsUrlBuilder.cs:13`). A grep finds zero remnants.
- **The Thunderstore dependency resolver** (`ThunderstoreDependencyResolver.cs`) is a
  textbook adaptive design: per-package API calls for small closures, one bulk community-index
  fetch once a closure looks modpack-sized (>15 packages), a 10-minute TTL cache, a 512-package
  tripwire, and it correctly implements the r2modman "pins are floors" convention — with a
  12-scenario test suite that asserts API-call counts.
- **The BepInEx family is data-driven, not hand-written** — ~200 games stood up from a
  vendored ecosystem schema with per-game install-rule routing, replacing the hand-written RoR2
  module rather than accreting per-game classes.
- **Local Steam recognition** (`LocalFileHasher`/`LocalGameVersionRecognizer`) hashes installed
  games against local depot manifests — no login, no CDN — skips size-mismatched files before
  reading a byte, computes all five hashes in one streaming pass, and records per-depot results
  incrementally so a cancelled run resumes.
- **`GuardAgainstVanishedGameFiles`** (`ALoadoutSynchronizer.cs:1052`) is exactly the right
  defense-in-depth: it refuses to sync when the SQL and C# hash read-paths disagree, blocking
  the "delete every vanilla file" failure class that a fork-introduced bug caused once and was
  fixed the same day.
- **The `ILibraryDownloadJob` seam** genuinely makes downloads source-agnostic — Thunderstore
  and mod.io plugged into the Downloads UI without touching the Nexus paths.

---

## 4. Efficiency & optimization

The good news first: the *core hot path* — applying a loadout — is well-architected. Disk I/O
is `O(changed files)`: the indexer stats every file in parallel but compares tick-precision
mtime+size against stored state and only hashes what changed (a real fix — the
`_0001_ConvertTimestamps` migration shows a timestamp-precision rehash-churn bug was already
found and repaired). `GetDiskStateForGame` is a hand-rolled sorted merge-join over lightweight
datom iterators with a UTF-8 string pool, which is the right shape for a 100k-file game. The
winning-file computation is a single set-level DuckDB query, not N+1 entity loads. This is the
part most likely to dominate user-perceived performance, and it's solid.

The measurable inefficiencies worth fixing, roughly in impact order:

### High-impact

- **`O(N³)` plugin sort + allocation storm** (`Sorter.cs:197` + `PluginsFile.cs:65`,
  *verified*). For Creation Engine games, `plugins.txt` is regenerated on *every* apply. The
  `Sorter` invokes the rule function `O(N²)` times with no memoization, and each call
  allocates an `After` record per master. A 500-plugin Skyrim load-out ≈ 250k rule-creator
  calls × ~150 allocations = ~37M short-lived objects **per apply**; at 1,000 plugins it's a
  multi-second GC-thrashing stall. The verifier found the inner scan is *provably 100% wasted*
  for `plugins.txt` (it only consumes `First`/`Before` rules, which `RuleCreator` never
  emits), so the memoization fix is trivially safe.
- **Thunderstore index downloads uncompressed** (`HttpDownloader/Services.cs:37`, *verified*).
  The shared `SocketsHttpHandler` never sets `AutomaticDecompression`, so the multi-megabyte v1
  community index — which the docs literally describe as "megabytes gzipped" — is served
  *identity-encoded*, a 5–20× bandwidth/latency penalty on the modpack-install critical path
  for big communities (Lethal Company, RoR2). One-line fix:
  `AutomaticDecompression = DecompressionMethods.All`, which also speeds up all Nexus/GraphQL
  JSON traffic.
- **`O(n·m)` overrides-group scans during sync** (`ALoadoutSynchronizer.cs:963` & `:787`,
  *verified*). For unknown-version games the entire install lands in the overrides group; a
  later update touching 5k files against a 50k-entry group re-materializes entities
  quadratically, all while holding the synchronizer's exclusive lock. Fix: load the group's
  children once into a `Dictionary<GamePath, EntityId>`.
- **Full GC after every sync** (`ALoadoutSynchronizer.cs:529`, *verified*). Every apply is
  followed by a full garbage collection — parse every `.nx` header, scan every archive entry in
  the DB, synchronously repack. This should be debounced/scheduled, not run inline on every
  sync.
- **Loadout switching resets to vanilla then re-extracts everything**
  (`ALoadoutSynchronizer.cs:1021`, *verified*) instead of diffing the two loadout trees the
  code is already perfectly capable of diffing.

### Medium-impact

- **Plugin headers re-parsed from the file store on every apply** with no hash-keyed cache
  (`SkyrimSE.cs:99`) — Bannerlord's manifest pipeline does exactly this caching correctly, so
  the pattern to copy already exists in-repo.
- **Nexus up-to-date pages never bump `DataUpdatedAt`** (`PerFeedCacheUpdaterResult.cs:20`), so
  every tracked mod page is fully re-fetched (2 GraphQL calls each) every 28 days even when the
  feed proves nothing changed.
- **`IsAlreadyDownloaded` does 2–3 network round-trips before the cheap local checks**
  (`NexusModsLibrary.cs:168`) in the one-click flow.
- **Dense priority renumbering** rewrites every group's `Priority` datom on each mod removal or
  conflict action (`LoadoutManager.TxFuncs.cs:38`), growing event-sourced history `O(mods)` per
  operation.
- **Downloaded files fully re-read for hashing**, and external collection downloads compute MD5
  a *second* time (`AddLibraryFileJob.cs:85`) — up to two redundant full passes over multi-GB
  archives. The `StreamProgressWrapper` already in the pipeline could hash while downloading.
- **Steam `DepotChunkProvider` leaks every rented 1 MB ArrayPool buffer** to the GC
  (`DepotChunkProvider.cs:57`); **GOG's block cache is unbounded** (`Client.cs:532`); the Steam
  CLI indexer does `O(n·m)` `ConcurrentBag.Contains` per file (`Verbs.cs:313`).
- **UI at scale:** eager root-model activation defeats TreeDataGrid virtualization
  (`TreeDataGridAdapter.cs:153`) — a 1,000-mod library spins up the full subscription graph and
  fires ~1,000 concurrent thumbnail loads on page open; the search filter synchronously
  materializes all child rows on the UI thread (`Filter.cs:39`); `FilterLoadoutItems` observes
  *every* Nexus mod page in the whole datastore, missing the per-game pre-filter the library
  side already has (`NexusModsDataProvider.cs:344`).

### The one strategic efficiency issue

**Event-sourced history grows without bound** (`UndoService.cs:36`, *architecture*). Nothing
compacts the main store; every sync appends disk-state deltas for up-to-100k-file games, and
every apply permanently becomes an undo restore point that the revision list later pays for
with an `AsOf`-snapshot `COUNT` per revision. The undo feature *depends* on history, so this
needs a retention policy, not blind compaction — but today there is neither.

---

## 5. Mod-source extensibility (adding more sources)

This is the area most directly tied to your goal, and it's the one with the biggest gap
between how good the *component* engineering is and how *repeatable* adding a source is.

### The reality today: three vertical silos over two good horizontal seams

There are two genuinely source-agnostic seams, and they work well:

1. **Downloads** — every source's job implements `ILibraryDownloadJob`, so `DownloadsService`
   and the Downloads page observe/pause/resume/cancel any source's transfer without knowing
   which source it is, and all three reuse the resumable `HttpDownloadJob` base.
2. **Protocol dispatch** — `UriSchemeRegistration` auto-registers the OS scheme for every
   enabled `IIpcProtocolHandler` in DI, so the OS-integration side of a new `nxm://`-style link
   is already zero-touch.

**Everything above those two seams is per-source hand-wiring.** The newer integrations were
literally built by copying the previous one — `ModIoDataProvider` says *"Modeled on
ThunderstoreDataProvider"* in its doc comment, and the two files are ~90% line-identical.

### What a 4th source (Modrinth) must touch *today*

Measured empirically: the mod.io PR (`f19d71e`) — the *smallest possible* source, with no
protocol handler, no update checks, no real game association, no thumbnails — touched **38
files / 1,634 lines**. About ~1,300 of those lines are intrinsic per-source work no abstraction
removes (models, DTOs, API client, download job, facade, CLI verbs, tests). But ~300 lines
across ~10 seams are *recurring wiring an abstraction would eliminate*:

- `LibraryViewModel.cs:252` — three differently-shaped capability checks
  (`HasNexusModsSource` via nullable id, `HasThunderstoreSource` via interface,
  `HasModIoSource` via interface + settings gate)
- `ILibraryViewModel.cs:43` — per-source `Open*Command` / `Has*Source` members
- `LibraryView.axaml:152` + `.axaml.cs:116` — per-source menu items, empty-state buttons, ~17
  hand-written bindings
- `App.UI/Services.cs:308` — two DI registrations per source
- `App/Services.cs:100`, `App.Cli/Services.cs:38` — composition + protocol handler
- `LibraryItemRemovalInfo.cs:27` — a redownloadable type-switch whose result flag is *literally
  named `IsNexus`* for all sources
- `DownloadsDataProvider.cs:106`, `IconValues.cs` — thumbnails + icon
- a copied ~190-line data provider, registered twice
- `IModrinthGame` capability on every supported game class

And the "forgotten switch" hazard is **already manifest**: `DownloadsDataProvider.cs:106` only
loads `NexusModsFileMetadata`, so Thunderstore and mod.io downloads always show the fallback
thumbnail. Both non-Nexus providers hardcode `GetAllFiles(...) => []`, so "Remove game & delete
downloads" silently misses their archives and the per-game Downloads view never shows them
(`ThunderstoreDataProvider.cs:36`) — even though the app-side game is fully derivable at
job-creation time (`NexusModsDownloadJob.Create` already demonstrates the exact lookup).

There's also one genuine **layering inversion**: `Apocrypha.Library` (a core service assembly)
references `Apocrypha.App.UI`, and App.UI references the concrete `Networking.NexusWebApi`
assembly directly (because `NexusModsLibrary`, unlike its two younger peers, has no interface).
Fix this before it calcifies — every new source inherits it.

### Concrete proposal: a first-class mod-source interface set

Create `src/Apocrypha.Abstractions.ModSources`, referenced by App.UI, App.Cli, and each
source's abstractions project. The reviewer worked out a specific shape that maps cleanly onto
the existing code:

```csharp
// Capability + UI surface — kills the Has*/Open* members and the axaml per-source blocks
interface IModSource {
    ModSourceId Id;  string DisplayName;  IconValue Icon;  bool IsEnabled;
    bool SupportsGame(IGameData game);
    Uri? GetBrowseUri(IGameData game);
}
// Unifies pasted links (mod.io) AND protocol URLs (nxm/ror2mm) — moves IIpcProtocolHandler
// impls out of App.Cli into each source's networking project as thin adapters
interface IModSourceLinkHandler {
    bool TryParse(string input, out ModSourceRequest req);
    Task<LibraryFile.ReadOnly?> HandleAsync(ModSourceRequest req, CancellationToken ct);
}
// Source-agnostic face of the three facades
interface IModSourceLibrary {
    bool IsDownloaded(EntityId metadataId, IDb db);
    Task<IJobTask<ILibraryDownloadJob, AbsolutePath>> CreateDownloadJob(EntityId, CancellationToken);
}
// Fixes the Downloads icon switch, disabled "View mod page", and GameId=default in ONE seam
interface IModSourceMetadataResolver {
    Optional<EntityId> GetThumbnailKey(EntityId metadataId, IDb db);
    Optional<Uri>      GetModPageUri(EntityId metadataId, IDb db);
    Optional<GameId>   GetGameId(EntityId metadataId, IDb db);
}
interface IModSourceUpdateChecker { /* optional capability — update checking is Nexus-typed today */ }
```

Each source registers these from its own `Services.cs` via `services.AddModSource<T>()`; the
Library page, Downloads page, removal logic, and protocol dispatch then **enumerate the
collection** instead of hardcoding three names. In App.UI, collapse the copied providers into an
abstract `SourceLibraryDataProviderBase<TItem>` — MnemonicDB's generated models prevent *full*
genericity, but everything below `ObserveGameLibraryItems()` + three small accessors (name,
version, metadata id) is already source-independent.

**Keep the per-source MnemonicDB metadata models exactly as they are** — that part of the
design is sound, and the capability interfaces (`IThunderstoreCommunityGame`, `IModIoGame`)
correctly decouple games from sources. The missing piece is purely the runtime/UI-facing
interface set. Do this *before* Modrinth: Modrinth will want dependency resolution (like
Thunderstore) **and** per-game scoping (like mod.io) **and** update checks (like Nexus), so it
will exercise every seam the current three collectively expose.

---

## 6. Mod loading & load-order efficiency

There are three distinct load-ordering systems in the repo, which is itself a finding — they
should converge:

1. **The v0.19 "Load Order Rework"** (`SortOrder`/`SortOrderItem` entities, one
   `ASortOrderVariety` per game concern, managed by `SortOrderManager`). Only Cyberpunk's REDmod
   variety exists today; every other game registers an empty array.
2. **The file-conflict priority system** (`LoadoutItemGroupPriority`, dense integer priority per
   mod group) driving winning-file resolution in SQL and the drag&drop FileConflicts page.
3. **A legacy rule-based topological `Sorter`** (First/Last/Before/After) used only by the
   fork-added `plugins.txt` writer and collection download rules.

### The rework's core write path is genuinely good

A drag&drop is *one* read of the current order, an in-memory splice, and a *single* transaction
that persists only the changed `SortIndex` datoms, guarded by an optimistic-concurrency
transaction function that compares max `TxId`s — **no per-item DB round trips**. It's
collection-aware from day one (`SortOrder.ParentEntity` is Loadout-or-CollectionGroup), and
REDmod deploy reconciles in memory at deploy time so the emitted modlist is correct even if the
persisted order is stale. Compared with the pre-rework in-memory-provider approach, this is a
real improvement: persistent, transactional, collection-aware, reactive.

### But the lifecycle has real bugs (the most-corroborated issues in the whole review)

- **The singleton clobber** (`SortOrderManager.cs:87`, *verified* — found independently by the
  load-order, Thunderstore, *and* datamodel reviewers). `SortOrderManager` is a process-wide
  singleton, and `RegisterSortOrderVarieties` **replaces** its state
  (`_sortOrderVarieties = ...ToFrozenDictionary()`). Every game resolves the *same* instance;
  Cyberpunk registers `[RedModSortOrderVariety]`, all others register `[]`. Because the
  registration fires from the loadout page's constructor, **opening any other game's loadout
  page after Cyberpunk's wipes Cyberpunk's varieties for the rest of the session** — the REDmod
  load-order UI silently vanishes, *and* the still-live change subscription reconciles against
  an empty set, so RedMod add/remove stops updating the persisted order that deploy reads. Fix:
  key varieties by `GameId` inside the singleton (merge, don't replace), or register one manager
  per game.
- **`CopyLoadout` loses load order and all conflict priorities**
  (`LoadoutManager.cs:189`, *verified*, found by 2 reviewers). It only remaps `LoadoutItem`
  entities; `SortOrder`/`SortOrderItem` and `LoadoutItemGroupPriority` are separate models and
  are never copied. A cloned Cyberpunk loadout loses its hand-tuned REDmod order, and — because
  the clone has *zero* priority rows — every conflict resolves by nondeterministic `arg_max`
  tie-break, so **the clone can deploy different winning files than the original** and the
  FileConflicts page shows nothing. `CloneCollection` already does this copy correctly, which
  proves it's an omission, not a design choice.
- **`plugins.txt` order is nondeterministic between applies** (`PluginsFile.cs:50`, *verified*).
  The `Sorter` is called with no tie-break comparer, so plugins with no master relationship are
  emitted in `ConcurrentDictionary` order — which depends on per-process-randomized string
  hashes. A 40-ESP Skyrim setup gets a *different* `plugins.txt` on every launch with no user
  action, silently reshuffling record-conflict winners. (The verifier corrected the reviewer's
  save-corruption claim: SkyrimSE/FO4 remap FormIDs by name, so saves don't directly break — but
  the game-behavior churn is real.)

### The perf cliff and the Bethesda-readiness gap

The `O(N³)` plugin sort (Section 4) lives here. And **readiness for Bethesda load order is the
weakest area**: the masters-before-dependents machinery exists (`PluginsFile` builds `After`
rules from Mutagen master references), but there is **no** `SortOrderVariety` for Creation
Engine — SkyrimSE/FO4 register `[]`, so users have *zero* control over plugin order,
`PluginsFile.Ingest` is a no-op TODO (LOOT/manual edits are silently clobbered), and ordering is
keyed on file *extension* rather than header flags (so an ESM-flagged `.esp` will be misordered).
Before wiring Bethesda into the SortOrder framework: add a content-hash-keyed plugin-metadata
cache, give the `Sorter` single-evaluation rule memoization plus a deterministic comparer, and
add a CreationEngine variety that owns `plugins.txt` with the topo sort demoted to a
validation/auto-sort assist over a user-editable order.

### Smaller load-order items

- **The reconcile trigger is too broad** (`SortOrderQueries.sql:7`) — it observes a per-*file*
  changeset for the whole game, so any unrelated file change re-runs the joins and commits a
  reconcile transaction even when nothing changed.
- **REDmod deploy is silently skipped on Linux for non-Steam stores**
  (`RedModDeployTool.cs:82`) — so GOG/Heroic Cyberpunk installs never get their load order
  applied, on a Linux-first fork.
- **`MoveItems` crashes instead of logging** when an item is missing
  (`ASortOrderVariety.cs:105`), and **silently discards the user's drag** when the
  optimistic-concurrency guard fires with no retry (`:147`).
- **The whole SortOrder machinery is inert until some UI touches `game.SortOrderManager`** —
  reconciliation and orphan cleanup don't run at all until the user opens a Rules tab.

---

## 7. Prioritized roadmap

Ordered by (impact × reachability) ÷ effort. The first tier is small, high-confidence fixes.

> **✅ Status (2026-07-14): Tier 0 and all of Tier 1 are implemented** on branch
> `claude/repo-improvements-analysis-ufkkla` — see [`CODE_REVIEW_FIXES.md`](./CODE_REVIEW_FIXES.md)
> for the per-fix write-up and local verification steps. Not yet compiled/tested (the authoring
> environment had no .NET SDK). One exception: migration `_0009` (#6) is fixed but deliberately left
> **unregistered** pending a legacy-DB test. Tier 2+ below is still open.

### Tier 0 — security (added by the completeness pass; do before the rest) — ✅ implemented

0. 🔴 **Sanitize FOMOD destination paths** (`FomodXmlInstaller.cs:173/236/254`, +`RemoveRoot:178`).
   Reject or normalize-and-contain `..` (route destinations through `PathsHelper.FixPath` the same
   way `ManagedZipExtractor` does), and add a defense-in-depth containment assertion in the
   synchronizer before any on-disk write. Confirmed exploitable end-to-end: a malicious mod's
   `ModuleConfig.xml` can write attacker bytes outside the game directory on apply. Add a directed
   test that a `..`-laden destination is rejected. *This is the highest-priority item in the review.*

### Tier 1 — do first (small, verified, user-visible or data-correctness) — ✅ implemented (#6 registration gated)

1. **Fix the `SortOrderManager` singleton clobber** (`SortOrderManager.cs:87`). Key varieties
   by `GameId`. Restores REDmod load order that silently breaks in any multi-game session.
2. **Make `CopyLoadout` copy `SortOrder`/`SortOrderItem` + `LoadoutItemGroupPriority`**
   (`LoadoutManager.cs:189`), mirroring `CloneCollection`. Prevents silent divergence of a
   cloned loadout's winning files. Add the regression test.
3. **Enable HTTP decompression** — `AutomaticDecompression = DecompressionMethods.All` in
   `HttpDownloader/Services.cs:37`. One line; 5–20× faster modpack resolution + faster all JSON.
4. **Fix the resume-corruption bug** (`HttpDownloadJob.cs:120`): on a non-range retry with
   bytes already written, reset position/length to 0. Silent archive corruption today, no user
   action required to trigger.
5. **Give the `Sorter` a default tie-break comparer** (`PluginsFile.cs:50`) so `plugins.txt` is
   deterministic across applies.
6. **Fix or remove migration `_0009`** (`SchemaVersions/Services.cs:15`): it's unregistered *and*
   references a nonexistent SQL macro, so legacy loadouts silently tie all conflicts at
   priority 0 and are invisible to the conflicts UI. Define the macro, register it, add the
   legacy-DB test.
7. **Chunk `NxFileStore.BackupFiles`** (`NxFileStore.cs:196`) into bounded batches — it opens
   every file's stream at once, so a many-file mod or unknown-version game hits the Linux 1024
   fd limit and crashes mid-manage. Add a `try/finally` so a partial failure doesn't leak fds.

### Tier 2 — the strategic items

8. **Stand up a fork-owned hash-DB update feed** and flip `EnableRemoteUpdates` back on
   (`FileHashesServiceSettings.cs:29`). This is the single most important piece of unfinished
   strategic work — every game patch since 2025-09-30 currently degrades the default experience.
   Even a GitHub-releases bucket refreshed by a scheduled workflow using the existing
   `BuildHashesDb` verb would do it. Until then, ship refreshed snapshots with each release.
9. **Introduce the `IModSource` interface set** (Section 5) *before* Modrinth. Cuts ~300 lines /
   ~10 seams off every future source and fixes the already-wrong Downloads thumbnails + game
   association for the existing three.
10. **Memoize the plugin `Sorter`** (`Sorter.cs:197`) — evaluate `ruleFn` once per item; turns
    `O(N³)` into `O(N²)` and kills ~37M allocations per apply on large Bethesda loadouts.
11. **Debounce the post-sync GC** (`ALoadoutSynchronizer.cs:529`) and **diff-based loadout
    switching** (`:1021`) instead of reset-to-vanilla-then-re-extract.

### Tier 3 — test the effectors that touch user data

12. Directed tests for the two anti-data-loss guards, the Proton `user.reg` writer (extract the
    string transform into a pure function and table-test it), the modpack orchestrator, the
    file-conflict priority TxFuncs, and the "Remove duplicates" selector — all unit-testable
    today against the existing harnesses.
13. **Resurrect or explicitly delete the dead CI lanes.** `RequiresNetworking` tests
    (collection installs, the HTTP downloader, Nexus API) *never run anywhere* — the PR/push
    lanes filter them out and the only lanes that run them are `workflow_dispatch`-only needing
    self-hosted runners + `NEXUS_API_KEY` the fork lacks. Convert what can be hermetic (the
    `LocalHttpServer` helper already exists but is unused), and quarantine the rest so the suite
    stops projecting coverage it doesn't deliver.

### Tier 4 — hardening

14. Synchronize the OAuth token refresh with a semaphore (`OAuth2MessageFactory.cs:57`) so 16
    parallel collection downloads can't force-log-out the user; fix the Steam `Session` state
    machine that hangs forever after a disconnect (`Session.cs:124`); fix the `OverlayController`
    lock-vs-dispatcher deadlock (`OverlayController.cs:51`); reset `user.reg` writing to the
    atomic temp-file+rename pattern the repo already uses in `CachedHttpStreamFactory`.
15. Move at-rest secrets (OAuth refresh token + API key are plaintext in the datastore) behind
    the OS keyring/Secret Service, and verify downloaded bytes against the source's advertised
    hash before install (mod.io even ships an MD5 the code currently drops).

### Tier 5 — completeness-pass follow-ups (new, high-value)

These come from the completeness critic (§8.1, full detail in `CODE_REVIEW_VERIFICATION.md` §3).
The FOMOD traversal is already Tier 0; the rest, roughly in impact order:

16. **Harden the FOMOD-in-collection flow** — it's the fork's most bug-dense new area:
    synchronize (or per-install-instance) the process-singleton `FomodXmlInstaller._delegates`
    that `Parallel.ForEachAsync` currently races (`FomodXmlInstaller.cs:112`); route FOMOD mods
    with null `choices` to a non-interactive path so no dialog pops up mid-unattended-install
    (`InstallCollectionDownloadJob.cs:141`); bound `PresetGuidedInstaller`'s step index
    (`:35`) and validate preset selections instead of name-only matching (`:38`). Add tests.
17. **Fix Linux game discovery beyond Steam** — set `Platform = Windows` in the EGS/GOG/Heroic-GOG
    locators for Wine installs (mirroring `SteamLocator`, `GOGLocator.cs:67` et al.), and add a
    Heroic/Legendary EGS locator so Epic-via-Heroic games are detectable (`ServiceExtensions.cs:41`).
18. **Evict per-loadout diagnostic observables** (`DiagnosticManager.cs:46`) so viewing a loadout's
    diagnostics doesn't leak a hot pipeline (with web-API calls) that re-runs on every DB change for
    the whole session; and skip building the sync tree for games whose emitters don't consume it.
19. **Bannerlord**: filter disabled modules out of the launch load order (`Helpers.cs:19`), and fix
    the `Modules/Multiplayer` backup-ignore typo (`BannerlordLoadoutSynchronizer.cs:31`).
20. **Harden untrusted binary/XML parsing** — bound the BG3 `.pak` header-driven allocations and use
    `ReadExactly` (`PakFileParser.cs:128/245`); set `DtdProcessing.Prohibit` on the Bannerlord
    `SubModule.xml` reader (`BannerlordModInstaller.cs:192`). Same class as the FOMOD write.
21. **GC safety**: make GC take a consistent snapshot under the write lock (or re-check refs after
    the snapshot) to close the dedup TOCTOU (`RunGarbageCollector.cs:25`), and add a fail-safe for
    unknown reference kinds instead of deleting them (`DataStoreReferenceMarker.cs:22`).

---

## 8. Notes on method & confidence

- **"verified"** findings survived an adversarial pass whose explicit job was to refute them by
  re-reading the source; their severities here already fold in the verifier's corrections.
- **The verification pass is now complete (2026-07-14).** The 37 findings that were still
  *unverified* at first publication have all been refuted-or-confirmed — **36 held (with several
  severity corrections), 1 was refuted** (`se3`, the 7z symlink-escape claim, disproven
  empirically by running the bundled `7zz` binary). Per-finding verdicts are in
  [`CODE_REVIEW_VERIFICATION.md`](./CODE_REVIEW_VERIFICATION.md) §2 and the appendix `Status`
  column below now reflects them.
- **Three findings were refuted** and are excluded from the confirmed counts: the two original
  refutations (a bulk-index "deprecated versions" claim that was overstated, and a
  reorder-rewrites-every-datom claim already covered by the dense-renumber finding), plus `se3`.
  One "critical" directory-delete claim was earlier downgraded to low after the verifier proved
  every reachable delete path re-indexes first.

### 8.1 Completeness critic — what the 14 dimensions missed

The final "what did everyone miss?" pass ran across seven blind spots (FOMOD, FOMOD-in-collection,
Epic/GOG locators, diagnostics emitters, Bannerlord, BG3/Larian `.pak`, and the GC / single-process
projects). It surfaced **~33 new code-backed findings, 7 of them high** — the single most important
result of the whole review:

- 🔴 **FOMOD arbitrary file write (path traversal) — HIGH, arguably critical.** A malicious
  FOMOD's `ModuleConfig.xml` destination like `..\..\..\<path>` is **not** sanitized —
  `RelativePath.FromUnsanitizedInput` doesn't strip `..`, and every downstream hop
  (`Join`/`Combine`/`NxFileStore.ExtractFiles`) is pure concatenation, so the OS resolves the `..`
  and writes attacker-controlled bytes **outside the game directory** on apply → potential code
  execution. Traced end-to-end and confirmed exploitable (`FomodXmlInstaller.cs:173/236/254`). The
  repo already has the fix pattern (`ManagedZipExtractor` runs names through `PathsHelper.FixPath`);
  the FOMOD path just skips it. **This is finding #0 on the roadmap.**
- **FOMOD-in-collection flow** (the fork's most bug-dense new area): an interactive installer
  window pops up mid-*unattended* collection install when `choices` is null; the process-singleton
  installer's delegates are raced across `Parallel.ForEachAsync` (mod A applies mod B's choices);
  an unbounded preset step index crashes; name-only preset matching silently drops required files.
- **Linux game discovery beyond Steam**: EGS/GOG locators never set `Platform` (Windows-via-Wine
  mislabeled native → wrong exe), and there's no Heroic/Legendary EGS locator at all (Epic-via-Heroic
  games undetectable).
- **Diagnostics**: `DiagnosticManager` never evicts per-loadout observables, leaking a hot pipeline
  (with web-API calls) that re-runs the full pass on every DB change for every loadout viewed.
- Plus Bannerlord (disabled modules injected into launch order; a `Modules/Multiplayer` typo), BG3
  `.pak` parsing (partial-read false-corruption, header-driven OOM DoS), and GC (stale-snapshot
  TOCTOU, fail-open reference marking). Full list with locations in
  [`CODE_REVIEW_VERIFICATION.md`](./CODE_REVIEW_VERIFICATION.md) §3; all are folded into the
  appendix below and tagged `completeness`.

A recurring theme: **untrusted mod-file parsing is systematically under-hardened** — the FOMOD
write, the Bannerlord `SubModule.xml` billion-laughs DoS, and the BG3 `.pak` allocation DoS are the
same class of missing guard on third-party input.

The full findings table follows (now including the completeness findings).

---

## Appendix — full findings table

Severities reflect verifier corrections where a verification pass ran. `verified` = survived
adversarial refutation; `unverified` = reviewer finding, not adversarially re-checked; `REFUTED` =
disproven by the refutation pass; `completeness` = newly found by the completeness critic (§8.1).
As of 2026-07-14 every finding that was in scope for verification has a verdict.

| Sev | Status | Cat | Finding | Location |
|-----|--------|-----|---------|----------|
| HIGH | verified | bug | OverlayController deadlocks the UI: synchronous Dispatcher.UIThread.Invoke while holding a lock the UI thread also takes | `src/Apocrypha.App.UI/Overlays/OverlayController.cs:51` |
| HIGH | verified | bug | Steam Session state machine hangs forever after disconnect or auth failure; callback exceptions are silently swallowed | `src/Apocrypha.Networking.Steam/Session.cs:124` |
| HIGH | verified | bug | Conflict-priority backfill migration is unregistered and references a nonexistent SQL macro | `src/Apocrypha.DataModel.SchemaVersions/Services.cs:15` |
| HIGH | verified | bug | Singleton SortOrderManager state is clobbered by each game's variety registration | `src/Apocrypha.Abstractions.Games/SortOrder/SortOrderManager.cs:87` |
| HIGH | verified | bug | Resume/retry against a server without range support writes the full response body at a non-zero offset, silently corrupting the download | `src/Apocrypha.Networking.HttpDownloader/HttpDownloadJob.cs:120` |
| HIGH | verified | bug | Singleton SortOrderManager registration is last-writer-wins: opening another game's Rules UI wipes Cyberpunk's registered sort-order varieties for the session | `src/Apocrypha.Abstractions.Games/SortOrder/SortOrderManager.cs:87` |
| HIGH | verified | bug | CopyLoadout silently loses the user's load order and all file-conflict priorities | `src/Apocrypha.DataModel/LoadoutManager.cs:189` |
| HIGH | verified | bug | plugins.txt order is nondeterministic between applies: Sorter ties resolved by racy ConcurrentDictionary order with no comparer | `src/Apocrypha.Games.CreationEngine/PluginsFile.cs:50` |
| HIGH | verified | perfor | O(N^3) work and allocation storm sorting plugins: Sorter invokes ruleFn O(N^2) times and PluginsFile.RuleCreator allocates O(N) rules per call | `src/Apocrypha.DataModel/Sorting/Sorter.cs:197` |
| HIGH | verified | extens | No first-class mod-source abstraction: adding a source requires edits at ~10 seams across 6+ projects | `src/Apocrypha.App.UI/Pages/LibraryPage/LibraryViewModel.cs:252` |
| HIGH | verified | bug | Unsynchronized concurrent OAuth token refresh can force-logout the user mid-download | `src/Apocrypha.Networking.NexusWebApi/Auth/OAuth2MessageFactory.cs:57` |
| HIGH | verified | bug | Collections referencing a deleted/hidden Nexus file cannot be added (NotImplementedException) | `src/Apocrypha.Networking.NexusWebApi/NexusModsLibrary.Collections.cs:518` |
| HIGH | verified | perfor | Thunderstore v1 index downloads uncompressed: shared HttpClient never negotiates gzip | `src/Apocrypha.Networking.HttpDownloader/Services.cs:37` |
| HIGH | verified | bug | 200 BepInEx games overwrite the shared SortOrderManager singleton's varieties (last-write-wins) | `src/Apocrypha.Games.BepInEx/GenericBepInExGame.cs:74` |
| HIGH | verified | bug | Case-insensitive GamePath identity on case-sensitive Linux filesystems; SQL layer disagrees | `src/Apocrypha.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs:253` |
| HIGH | verified | bug | CopyLoadout silently drops conflict priorities and sort orders | `src/Apocrypha.DataModel/LoadoutManager.cs:189` |
| HIGH | verified | perfor | O(n*m) linear scans of the overrides group during ingest and reified delete | `src/Apocrypha.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs:963` |
| HIGH | verified | bug | BackupFiles opens every source file stream simultaneously — fd exhaustion crash on Linux | `src/Apocrypha.DataModel/NxFileStore.cs:196` |
| MEDI | verified | bug | Sync sacrifices unrestorable originals over the 5GB backup cap — narrowed to extract-overwritten, non-deduped, skipped files (pure deletions left on disk); logged, not silent | `src/Apocrypha.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs:1239` |
| MEDI | verified | archit | All reference data is frozen at vendoring time and remote updates are disabled — the default experience decays with every game patch (degrades gracefully to embedded snapshot) | `src/Apocrypha.Games.FileHashes/FileHashesServiceSettings.cs:29` |
| HIGH | verified | bug | Unresponsive-process kill hack can SIGKILL a busy-but-healthy main (entire tree) or an innocent recycled-PID process | `src/Apocrypha.App/Program.cs:173` |
| MEDI | verified | bug | Second GUI launch of an already-running app crashes with an unhandled exception instead of focusing the running instance | `src/Apocrypha.App/Program.cs:79` |
| HIGH | verified | bug | Both fork-added anti-data-loss guards in the synchronizer are completely untested | `src/Apocrypha.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs:1052` |
| MEDI | verified | bug | Proton user.reg registry rewriting has zero tests despite mutating a prefix-critical file (coverage gap; hides fd4) | `src/Apocrypha.Games.BepInEx/RunBepInExGameTool.cs:47` |
| MEDI | verified | bug | 'Remove duplicates' deletes library items via an untested selection pipeline | `src/Apocrypha.App.UI/Pages/Library/LibraryDuplicateFinder.cs:22` |
| HIGH | verified | bug | Modpack install orchestration (ror2mm handler) — including the duplicate-download prevention it exists for — has no tests | `src/Apocrypha.App.Cli/Types/IpcHandlers/Ror2mmIpcProtocolHandler.cs:70` |
| MEDI | verified | archit | Collection-install end-to-end tests never run anywhere: the RequiresNetworking partition is dead in this fork (documented/intentional) | `tests/Apocrypha.Collections.Tests/CollectionInstallTests.cs:16` |
| MEDI | verified | bug | The download pipeline every mod source depends on has one test, and it never runs (RequiresNetworking dead lane) | `tests/Networking/Apocrypha.Networking.HttpDownloader.Tests/HttpDownloadJobWorkerTests.cs:20` |
| HIGH | verified | bug | File-conflict priority machinery (which mod wins on disk) has no directed test | `src/Apocrypha.DataModel/LoadoutManager.TxFuncs.cs:22` |
| HIGH | verified | archit | Real FileHashesService is stubbed out of every game test — the subsystem that already caused install-deletion has no direct coverage | `src/Apocrypha.Games.FileHashes/FileHashesService.cs:1` |
| HIGH | verified | bug | Serializing TextEditorPageContext (OneOf<...>) throws, silently preventing the entire window layout from saving | `src/Apocrypha.App.UI/Pages/TextEdit/TextEditorPage.cs:15` |
| MEDI | verified | bug | Search filter synchronously materializes all child rows on the UI thread and returns stale/missing results | `src/Apocrypha.App.UI/Controls/TreeDataGrid/Filters/Filter.cs:39` |
| HIGH | verified | perfor | All root models are activated eagerly, defeating virtualization: full subscription graph + thumbnail load per row on page open | `src/Apocrypha.App.UI/Controls/TreeDataGrid/TreeDataGridAdapter.cs:153` |
| MEDI | verified | bug | InMemoryStore reads a plain Dictionary unsynchronized while a background cleanup task mutates it | `src/Apocrypha.Sdk/Resources/Caching/InMemoryStore.cs:118` |
| MEDI | verified | bug | Job pause/resume lifecycle races: double execution of a resumed job and cancelled-while-paused jobs leaking in the monitor | `src/Apocrypha.Backend/Jobs/JobMonitor.cs:105` |
| MEDI | verified | bug | FOMOD installer: ContinueWith reads task.Result without checking status — a faulted user-choice task strands the install forever; cancellation token ignored | `src/Apocrypha.Games.FOMOD/CoreDelegates/UiDelegate.cs:149` |
| MEDI | verified | perfor | Dense priority renumbering rewrites every group's priority datom on each removal/conflict action | `src/Apocrypha.DataModel/LoadoutManager.TxFuncs.cs:38` |
| MEDI | verified | bug | Quick pause→resume races in JobContext/JobMonitor: resume silently dropped, or a running job evicted from the monitor | `src/Apocrypha.Backend/Jobs/JobMonitor.cs:164` |
| MEDI | verified | perfor | Downloaded files are fully re-read for hashing, and external downloads compute MD5 twice — up to two redundant full passes per archive | `src/Apocrypha.Library/AddLibraryFileJob.cs:85` |
| MEDI | verified | bug | Duplicate-download prevention from PR #62 was not applied to the Nexus nxm handler — overlapping clicks still download twice | `src/Apocrypha.App.Cli/Types/IpcHandlers/NxmIpcProtocolHandler.cs:164` |
| MEDI | verified | perfor | Steam DepotChunkProvider never returns rented ArrayPool buffers — 1 MB garbage per downloaded chunk | `src/Apocrypha.Networking.Steam/DepotChunkProvider.cs:57` |
| MEDI | verified | perfor | Steam CLI indexer does O(n·m) ConcurrentBag.Contains per file — quadratic on large games | `src/Apocrypha.Networking.Steam/CLI/Verbs.cs:313` |
| MEDI | verified | perfor | Plugin headers re-parsed from the file store on every apply and every diagnostics run — no hash-keyed cache (unlike Bannerlord) | `src/Apocrypha.Games.CreationEngine/SkyrimSE/SkyrimSE.cs:99` |
| MEDI | verified | bug | User's drag&drop is silently discarded when the optimistic-concurrency guard fires — no retry in MoveItems/MoveItemDelta | `src/Apocrypha.Abstractions.Games/SortOrder/ASortOrderVariety.cs:147` |
| MEDI | verified | perfor | Sort-order trigger observes a per-file changeset for the whole game and reconciles (with a commit) on every unrelated file change | `src/Apocrypha.Abstractions.Games/SortOrder/SortOrderQueries.sql:7` |
| MEDI | verified | bug | REDmod deploy silently skipped on Linux for non-Steam stores — load order never applied for GOG/Heroic installs on a Linux-first fork | `src/Apocrypha.Games.RedEngine/RedModDeployTool.cs:82` |
| MEDI | verified | bug | Thunderstore/mod.io downloads have no game association: 'Remove game & delete downloads' silently misses them and per-game Downloads scope never shows them | `src/Apocrypha.App.UI/Pages/ThunderstoreDataProvider.cs:36` |
| MEDI | verified | bug | ror2mm one-click modpack install reports success even when dependency downloads failed | `src/Apocrypha.App.Cli/Types/IpcHandlers/Ror2mmIpcProtocolHandler.cs:176` |
| MEDI | verified | bug | mod.io loadout items are missing the 'Move to collection' action (copy-divergence from ThunderstoreDataProvider) | `src/Apocrypha.App.UI/Pages/ModIoDataProvider.cs:175` |
| MEDI | verified | bug | Empty modfile object from mod.io crashes deserialization instead of reporting 'no released file' | `src/Apocrypha.Abstractions.ModIo/DTOs/ModIoDtos.cs:59` |
| MEDI | verified | bug | Paused mod.io downloads resume against an expired binary URL and fail permanently; DateExpires is parsed but never consulted | `src/Apocrypha.Networking.ModIo/ModIoDownloadJob.cs:61` |
| MEDI | verified | bug | Paste-a-link command holds the R3 command queue for the entire download; no in-flight claim like the one PR #62 built for Thunderstore | `src/Apocrypha.App.UI/Pages/LibraryPage/LibraryViewModel.cs:280` |
| MEDI | verified | bug | GraphQL requests never refresh the OAuth token and silently run unauthenticated after expiry | `src/Apocrypha.Networking.NexusWebApi/Services.cs:90` |
| MEDI | verified | bug | One-click nxm download failures before the try-block give the user no feedback | `src/Apocrypha.App.Cli/Types/IpcHandlers/NxmIpcProtocolHandler.cs:164` |
| MEDI | verified | bug | Collection items with no explicit rules get arbitrary, nondeterministic conflict order instead of manifest order | `src/Apocrypha.DataModel/LoadoutManager.cs:634` |
| MEDI | verified | bug | Collection conflict rules are never re-applied when items are installed individually | `src/Apocrypha.App.UI/Pages/CollectionDownload/CollectionDownloadViewModel.cs:482` |
| MEDI | verified | bug | Cyclic collection rules crash the install job after all mods are already installed | `src/Apocrypha.DataModel/Sorting/Sorter.cs:116` |
| MEDI | verified | perfor | Up-to-date mod pages never get DataUpdatedAt bumped, forcing a full re-fetch of every page every 28 days | `src/Apocrypha.Networking.ModUpdates/PerFeedCacheUpdaterResult.cs:20` |
| MEDI | verified | bug | Sync-over-async mapping cache blocks threads and throws opaque exceptions when offline | `src/Apocrypha.Networking.NexusWebApi/V1Interop/GameDomainToGameIdMappingCache.cs:80` |
| MEDI | verified | bug | user.reg (Wine HKCU hive) is rewritten in place non-atomically before launch | `src/Apocrypha.Games.BepInEx/RunBepInExGameTool.cs:87` |
| MEDI | verified | perfor | Index-fetch threshold counts duplicate queue entries, triggering the huge index download for ordinary mods | `src/Apocrypha.Networking.Thunderstore/ThunderstoreDependencyResolver.cs:102` |
| MEDI | verified | bug | MissingBepInExEmitter ignores enabled state: disabled loader pack suppresses the warning while the game launches unmodded | `src/Apocrypha.Games.BepInEx/Emitters/MissingBepInExEmitter.cs:32` |
| MEDI | verified | bug | Ingesting a file at a reified-delete path corrupts the DeletedFile entity | `src/Apocrypha.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs:973` |
| MEDI | verified | perfor | Full garbage collection (header parse + full DB scan + synchronous repack) runs after every sync | `src/Apocrypha.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs:529` |
| MEDI | verified | perfor | Switching loadouts resets the entire install to vanilla and re-extracts everything | `src/Apocrypha.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs:1021` |
| MEDI | verified | bug | DesktopPortalConnectionManagerWrapper never releases its semaphore and ignores the WaitAsync timeout result | `src/Apocrypha.Backend/OS/LinuxInterop.cs:255` |
| MEDI | verified | bug | ProcessRunner.RunAsync(Process): non-Try TCS completion plus Kill-vs-Exited race throws from the cancellation callback | `src/Apocrypha.Backend/Process/Runner.cs:113` |
| MEDI | unverified | archit | Event-sourced history grows unboundedly with no compaction, and the undo revision list is N+1 over that history | `src/Apocrypha.DataModel/Undo/UndoService.cs:36` |
| MEDI | unverified | archit | No mechanism to refresh an expired download URL on resume; paused downloads and restarts are unrecoverable | `src/Apocrypha.Networking.HttpDownloader/HttpDownloadJob.cs:43` |
| MEDI | verified | bug | Legacy migration hijacks a coexisting genuine NexusMods.App install without consent | `src/Apocrypha.Sdk/LegacyDataMigration.cs:57` |
| MEDI | unverified | archit | FileHashes overlay retains a dual read-path — the same architecture that already deleted a game install once | `src/Apocrypha.Games.FileHashes/FileHashesService.cs:116` |
| MEDI | verified | bug | Proton user.reg patch inserts a duplicate winhttp value when the user has an explicit non-matching override, and races wineserver | `src/Apocrypha.Games.BepInEx/RunBepInExGameTool.cs:68` |
| MEDI | unverified | extens | No Bethesda load-order management: CreationEngine registers zero sort-order varieties, plugins.txt Ingest is a no-op, and ordering ignores plugin header flags | `src/Apocrypha.Games.CreationEngine/SkyrimSE/SkyrimSE.cs:58` |
| MEDI | verified | securi | Nexus OAuth refresh/access tokens and API key stored in plaintext at rest | `src/Apocrypha.Networking.NexusWebApi/Auth/JWTToken.cs:19` |
| MEDI | verified | securi | Downloaded mod bytes are not verified against the source's advertised hash for normal Nexus/Thunderstore/mod.io downloads (collection flows DO verify MD5/CRC32) | `src/Apocrypha.Networking.HttpDownloader/HttpDownloadJob.cs:194` |
| MEDI | REFUTED | securi | 7z/rar extraction can restore symlinks that escape the temp dir — REFUTED (empirically): bundled 7zz drops/blocks escaping links | `src/Apocrypha.Backend/FileExtractor/Extractors/SevenZipExtractor.cs:248` |
| MEDI | unverified | mainta | ThunderstoreDataProvider and ModIoDataProvider are ~90% duplicated 190-line files; a 4th source adds a 4th copy | `src/Apocrypha.App.UI/Pages/ModIoDataProvider.cs:27` |
| MEDI | unverified | mainta | The three library facades and download jobs re-implement the same lifecycle with no shared contract | `src/Apocrypha.Networking.ModIo/ModIoDownloadJob.cs:43` |
| MEDI | unverified | extens | Update checking is Nexus-typed end-to-end; no seam exists for other sources' updates | `src/Apocrypha.Networking.NexusWebApi/IModUpdateService.cs:47` |
| MEDI | unverified | archit | UI references networking implementation assemblies, and core Library references App.UI — inverted layering that every new source inherits | `src/Apocrypha.Library/Apocrypha.Library.csproj:8` |
| MEDI | unverified | extens | Downloads page thumbnail resolution hardcodes NexusModsFileMetadata — every non-Nexus download gets the fallback icon | `src/Apocrypha.App.UI/Pages/Downloads/DownloadsDataProvider.cs:106` |
| MEDI | unverified | extens | Protocol-handler registration and associate-verbs are hardcoded per scheme in App.Cli instead of contributed by sources | `src/Apocrypha.App.Cli/Services.cs:38` |
| MEDI | unverified | mainta | LibraryViewModel is a 1,466-line per-source workflow hub; Nexus collection-add round-trips through a hand-built nxm:// URL string | `src/Apocrypha.App.UI/Pages/LibraryPage/LibraryViewModel.cs:390` |
| MEDI | unverified | archit | Mod.io downloads and files have no app-side game association even though it is fully derivable, breaking game-scoped Downloads views and game-removal cleanup | `src/Apocrypha.Networking.ModIo/ModIoDownloadJob.cs:33` |
| MEDI | unverified | archit | No rate-limit (429/Retry-After) handling anywhere in the Nexus client | `src/Apocrypha.Networking.NexusWebApi/NexusApiClient.cs:162` |
| MEDI | unverified | archit | Systemic 'TODO: handle errors' + AssertHasData turns ordinary API errors into blind InvalidOperationExceptions | `src/Apocrypha.Networking.NexusWebApi/Errors/GraphQlResult.cs:60` |
| MEDI | unverified | extens | One-click modpack install stops at the Library: modpack roots are uninstallable and dependencies are never installed as a unit | `src/Apocrypha.Games.BepInEx/Installers/BepInExPluginInstaller.cs:119` |
| MEDI | unverified | extens | Schema drift silently unregisters family games; orphaned GameIds cannot even be stringified | `src/Apocrypha.Games.BepInEx/Schema/EcosystemSchemaParser.cs:73` |
| MEDI | verified | bug | nxm/ror2mm handoff drops links when main startup exceeds 60s and on stale-port SocketException | `src/Apocrypha.App/Program.cs:250` |
| MEDI | verified | bug | SettingsManager is not thread-safe: unsynchronized Dictionary mutated concurrently during startup | `src/Apocrypha.Backend/Settings/SettingsManager.cs:21` |
| MEDI | verified | bug | Fresh-install detection is broken: hosted services open the DB (and create its directory) before the modelExists check, making InitialSetup dead code | `src/Apocrypha.App/Program.cs:87` |
| MEDI | verified | bug | MultiProcessSharedArray constructor retries IOException in an unbounded 100% CPU busy-loop | `src/Apocrypha.SingleProcess/MultiprocessSharedArray.cs:35` |
| MEDI | verified | bug | URI scheme registration rewrites the desktop file and force-steals the default handler on every launch, once per scheme | `src/Apocrypha.App.Cli/UriSchemeRegistration.cs:29` |
| MEDI | verified | perfor | First game-detection scan (Steam/Heroic/Wine-registry parsing) runs synchronously on the UI thread at page activation | `src/Apocrypha.App.UI/Pages/MyGames/MyGamesViewModel.cs:117` |
| MEDI | verified | perfor | Unbounded blocking migration before first paint with no splash screen; startup .Wait(timeout) calls silently continue on timeout | `src/Apocrypha.App/Program.cs:109` |
| MEDI | unverified | archit | Undo restore points are not GC roots; per-sync GC can make reverts unrestorable | `src/Apocrypha.App.GarbageCollection.DataModel/DataStoreReferenceMarker.cs:26` |
| MEDI | verified | bug | Migration 0009 (conflict priorities) breaks the one-test-per-migration pattern | `src/Apocrypha.DataModel.SchemaVersions/Migrations/_0009_AddLoadoutItemGroupPriority.cs:38` |
| LOW | verified | perfor | Benchmarks measure none of the fork's hot paths and carry dead upstream artifacts (relevance opinion, not a defect) | `benchmarks/Apocrypha.Benchmarks/Benchmarks/EnumerateFiles.cs:21` |
| MEDI | unverified | mainta | Two CI test lanes are unrunnable corpses: nonexistent CLI verb, wrong SDK, missing runner | `.github/workflows/mod_install_tests.yaml:30` |
| MEDI | verified | bug | ThunderstoreLibrary.GetOrAddVersion has an untested find-or-create race — the DB-level root of the duplicate-entry bug | `src/Apocrypha.Networking.Thunderstore/ThunderstoreLibrary.cs:48` |
| MEDI | verified | perfor | FilterLoadoutItems observes every Nexus mod page in the whole datastore — missing the per-game pre-filter the library side has | `src/Apocrypha.App.UI/Pages/NexusModsDataProvider.cs:344` |
| MEDI | verified | bug | CustomSortComparer grids (load order, file conflicts) full-sort + Reset the TreeDataGrid and wipe selection on every changeset | `src/Apocrypha.App.UI/Controls/TreeDataGrid/TreeDataGridAdapter.cs:168` |
| MEDI | verified | bug | OverlayController performs synchronous Dispatcher.Invoke while holding its lock — cross-thread deadlock risk (DUPLICATE of the HIGH OverlayController finding) | `src/Apocrypha.App.UI/Overlays/OverlayController.cs:51` |
| MEDI | verified | bug | LoadoutViewModel selection-count subscription is never added to disposables — accumulates one live subscription per page activation | `src/Apocrypha.App.UI/Pages/LoadoutPage/LoadoutViewModel.cs:571` |
| LOW | verified | perfor | Remove-duplicates scans every LibraryFile entity (including nested archive entries) synchronously on the UI thread (bounded: explicit click, Drop re-entrancy guard) | `src/Apocrypha.App.UI/Pages/Library/LibraryDuplicateFinder.cs:26` |
| LOW | verified | bug | JobCancellationToken.Pause() disposes the CancellationTokenSource the running job is still using — pause can turn into a failed job or throw on the UI thread | `src/Apocrypha.Sdk/Jobs/JobCancellationToken.cs:97` |
| LOW | verified | bug | Get-or-create metadata races can mint duplicate Thunderstore/mod.io entities under the fork's own parallel downloader | `src/Apocrypha.Networking.Thunderstore/ThunderstoreLibrary.cs:48` |
| LOW | verified | bug | Tx-function equality ignores payload; MnemonicDB's HashSet dedup silently drops the second function in a transaction | `src/Apocrypha.DataModel/LoadoutManager.TxFuncs.cs:131` |
| LOW | verified | perfor | Per-item ObserveDatoms subscriptions in Thunderstore library view; count pipeline duplicates the whole chain | `src/Apocrypha.App.UI/Pages/ThunderstoreDataProvider.cs:66` |
| LOW | verified | perfor | NxFileStore.BackupFiles opens every file's stream eagerly — fd exhaustion for large mods on Linux | `src/Apocrypha.DataModel/NxFileStore.cs:196` |
| LOW | verified | bug | Resume re-runs HEAD and overwrites the saved ETag, defeating If-Range/If-Match stale-content detection | `src/Apocrypha.Networking.HttpDownloader/HttpDownloadJob.cs:284` |
| LOW | verified | perfor | GOG small-file-container block cache is unbounded in size — installs can hold the whole container in RAM | `src/Apocrypha.Networking.GOG/Client.cs:532` |
| LOW | verified | bug | GOG installer ZIP-magic scan misses headers straddling read boundaries | `src/Apocrypha.Networking.GOG/Client.cs:319` |
| LOW | verified | bug | MoveItems' missing-item guard dereferences an empty Optional and throws instead of logging, killing the drag&drop operation | `src/Apocrypha.Abstractions.Games/SortOrder/ASortOrderVariety.cs:105` |
| LOW | verified | bug | TxFunction Equals/GetHashCode collapse distinct operations: two different conflict-resolutions or rule-applications in one transaction dedupe silently | `src/Apocrypha.DataModel/LoadoutManager.TxFuncs.cs:75` |
| LOW | verified | perfor | IsAlreadyDownloaded performs network round-trips before local checks in the one-click flow | `src/Apocrypha.Networking.NexusWebApi/NexusModsLibrary.cs:168` |
| LOW | verified | bug | CleanDirectories recursively deletes directories that still contain untracked user files | `src/Apocrypha.Abstractions.Loadouts.Synchronizers/ALoadoutSynchronizer.cs:173` |
| LOW | verified | bug | GetGameFiles throws NotSupportedException for Xbox Game Pass (and any non-Steam/GOG/EGS store), crashing sync | `src/Apocrypha.Games.FileHashes/FileHashesService.cs:518` |
| LOW | unverified | perfor | GameDomainToGameIdMappingCache: synchronous interface forces sync-over-async GraphQL calls on cache miss | `src/Apocrypha.Networking.NexusWebApi/V1Interop/GameDomainToGameIdMappingCache.cs:80` |
| LOW | unverified | bug | NxFileStore.GetFileStream releases the read lock before the returned stream is consumed; HaveFile/Exists bypass the lock entirely | `src/Apocrypha.DataModel/NxFileStore.cs:398` |
| LOW | unverified | bug | TaskExtensions.FireAndForget loses exceptions when the passed CancellationToken is already cancelled | `src/Apocrypha.Sdk/Extensions/Task.cs:14` |
| LOW | unverified | bug | CliServer client-handler exceptions are unobserved and can instead surface as a faulted StopAsync during shutdown | `src/Apocrypha.SingleProcess/CliServer.cs:109` |
| LOW | unverified | mainta | SynchronizerService._statusObservables grows without bound and keeps DB-subscribed pipelines alive for deleted loadouts | `src/Apocrypha.DataModel/Synchronizer/SynchronizerService.cs:202` |
| LOW | unverified | bug | LaunchButtonViewModel mutates UI-bound reactive properties from a threadpool thread | `src/Apocrypha.App.UI/LeftMenu/Items/ApplyControl/LaunchButtonViewModel.cs:66` |
| LOW | unverified | bug | Transactions leaked on exception in InstallItem and UndoService.RevertTo | `src/Apocrypha.DataModel/LoadoutManager.cs:399` |
| LOW | unverified | archit | Scanning-migration version bump is non-atomic with the data rewrite | `src/Apocrypha.DataModel.SchemaVersions/MigrationService.cs:93` |
| LOW | unverified | extens | Thunderstore/mod.io sources return no files from GetAllFiles — orphaned library files when a game is removed | `src/Apocrypha.App.UI/Pages/ThunderstoreDataProvider.cs:36` |
| LOW | unverified | perfor | Thunderstore community backfill retries delisted packages every launch and holds one tx across all HTTP calls | `src/Apocrypha.Networking.Thunderstore/ThunderstoreCommunityBackfill.cs:48` |
| LOW | unverified | perfor | CloneCollection scans all priorities across all loadouts and double-loads every cloned entity | `src/Apocrypha.DataModel/LoadoutManager.cs:578` |
| LOW | unverified | bug | GitHubApi.FetchLatestRelease throws IndexOutOfRangeException when the repository has zero releases | `src/Apocrypha.Networking.GitHub/GitHubApi.cs:99` |
| LOW | unverified | perfor | Singleton download HttpClient has no PooledConnectionLifetime — stale DNS for the app's whole lifetime | `src/Apocrypha.Networking.HttpDownloader/Services.cs:37` |
| LOW | unverified | mainta | DownloadsService Update/Refresh path replaces DownloadInfo without moving subscriptions — latent frozen-row bug | `src/Apocrypha.Library/DownloadsService.cs:64` |
| LOW | unverified | bug | PerFeedCacheUpdater collapses duplicate mod pages by ModId — shadowed entities never marked for update | `src/Apocrypha.Networking.ModUpdates/PerFeedCacheUpdater.cs:63` |
| LOW | unverified | bug | Rebrand rewrote external doc-site URLs into links that never existed | `src/Apocrypha.Games.RedEngine/Cyberpunk2077/SortOrder/RedMod/RedModSortOrderVariety.cs:41` |
| LOW | unverified | mainta | CHANGELOG frozen at upstream v0.21.1 while the fork ships v0.1.0-v0.3.0 — the in-app changelog shows the wrong product | `src/Apocrypha.App.UI/Pages/Changelog/ChangelogPageViewModel.cs:20` |
| LOW | unverified | bug | games.json domain collisions resolved by arbitrary first-entry-wins | `src/Apocrypha.Networking.NexusWebApi/V1Interop/LocalMappingCache.cs:71` |
| LOW | unverified | bug | SortingSelectionViewModel detects new sort orders by comparing set COUNTS and never removes stale load-order views | `src/Apocrypha.App.UI/Pages/Sorting/SortingSelection/SortingSelectionViewModel.cs:108` |
| LOW | unverified | bug | GetSortOrderIdFor: unguarded duplicate-creation race yields permanent duplicate SortOrder entities that are only ever warned about | `src/Apocrypha.Abstractions.Games/SortOrder/ASortOrderVariety.cs:67` |
| LOW | unverified | securi | Unbounded extraction size allows a compression-bomb disk-exhaustion DoS on install | `src/Apocrypha.Backend/FileExtractor/Extractors/ManagedZipExtractor.cs:51` |
| LOW | unverified | securi | nxm:// query-parameter getters throw unguarded on malformed browser input | `src/Apocrypha.Abstractions.NexusWebApi/Types/NXMUrl.cs:24` |
| LOW | unverified | securi | Untrusted FOMOD ModuleConfig.xml is fed to a third-party XML script parser whose entity-expansion policy is not controlled in-repo | `src/Apocrypha.Games.FOMOD/FomodXmlInstaller.cs:119` |
| LOW | unverified | bug | Thunderstore UI surfaces ignore the EnableThunderstore setting — sources use three different gating conventions | `src/Apocrypha.App.UI/Pages/LibraryPage/LibraryViewModel.cs:253` |
| LOW | unverified | bug | GetOrAdd metadata paths are check-then-insert without uniqueness guards; concurrent callers can create duplicate metadata entities | `src/Apocrypha.Networking.Thunderstore/ThunderstoreLibrary.cs:55` |
| LOW | unverified | bug | CLI 'modio download' verb crashes with a raw stack trace on API errors it should render (invalid key 401, rate limit 429, bad download URL) | `src/Apocrypha.Networking.ModIo/CLI/Verbs.cs:88` |
| LOW | unverified | bug | GetOrAddFile is a check-then-insert race with no uniqueness constraint; duplicate metadata entities are permanent in the event-sourced store | `src/Apocrypha.Networking.ModIo/ModIoLibrary.cs:77` |
| LOW | unverified | perfor | No rate-limit awareness: 429s surface as 'Something went wrong', and every paste spends 3 API calls even for known mods | `src/Apocrypha.Networking.ModIo/ModIoApiClient.cs:69` |
| LOW | unverified | extens | Platform hardcoded to 'windows' inside the generic API client rather than as a per-game capability | `src/Apocrypha.Networking.ModIo/ModIoApiClient.cs:64` |
| LOW | unverified | securi | mod.io's published md5 filehash is discarded — downloads are never integrity-verified against the source | `src/Apocrypha.Abstractions.ModIo/DTOs/ModIoDtos.cs:59` |
| LOW | unverified | bug | Null-forgiven refresh reply can NRE; malformed refresh response escapes unhandled | `src/Apocrypha.Networking.NexusWebApi/Auth/OAuth2MessageFactory.cs:82` |
| LOW | unverified | mainta | Dead login check runs (and can hit the network) before every nxm URL dispatch, including OAuth callbacks | `src/Apocrypha.App.Cli/Types/IpcHandlers/NxmIpcProtocolHandler.cs:70` |
| LOW | unverified | perfor | Community index cache is never evicted and its lock serializes all fetches across communities | `src/Apocrypha.Networking.Thunderstore/ThunderstoreDependencyResolver.cs:39` |
| LOW | unverified | bug | GetOrAddVersion TOCTOU can create duplicate package/version metadata entities | `src/Apocrypha.Networking.Thunderstore/ThunderstoreLibrary.cs:55` |
| LOW | unverified | bug | Genuine cancellation surfaces as a failure toast: catch matches TaskCanceledException but not OperationCanceledException | `src/Apocrypha.App.Cli/Types/IpcHandlers/Ror2mmIpcProtocolHandler.cs:192` |
| LOW | unverified | perfor | Duplicate community lookups: resolver discards CommunityListings that ThunderstoreLibrary then re-fetches per package | `src/Apocrypha.Networking.Thunderstore/ThunderstoreLibrary.cs:85` |
| LOW | unverified | archit | Resolution errors abort the whole modpack install with no partial-install path | `src/Apocrypha.App.Cli/Types/IpcHandlers/Ror2mmIpcProtocolHandler.cs:102` |
| LOW | unverified | perfor | Community backfill re-queries permanently-unresolvable packages every launch and loses all progress on one mid-loop failure | `src/Apocrypha.Networking.Thunderstore/ThunderstoreCommunityBackfill.cs:48` |
| LOW | unverified | perfor | Dead GetIsUserLoggedInAsync call delays every protocol URL — including OAuth login callbacks — behind a network verify | `src/Apocrypha.App.Cli/Types/IpcHandlers/NxmIpcProtocolHandler.cs:70` |
| LOW | unverified | bug | DesktopPortalConnectionManagerWrapper never releases its semaphore and ignores the wait result | `src/Apocrypha.Backend/OS/LinuxInterop.cs:255` |
| LOW | unverified | mainta | StartCliBackend setting is exposed in the settings UI but has no effect; CliServer starts unconditionally | `src/Apocrypha.SingleProcess/CliSettings.cs:51` |
| LOW | unverified | perfor | Thunderstore community backfill re-fetches permanently-unresolvable packages serially on every launch | `src/Apocrypha.Networking.Thunderstore/ThunderstoreCommunityBackfill.cs:49` |
| LOW | unverified | bug | GetShouldSynchronize feeds the wrong 'previous' state into the merge, so the status pill can disagree with Apply | `src/Apocrypha.DataModel/Synchronizer/SynchronizerService.cs:79` |
| LOW | unverified | bug | No game-running guard on Linux — sync can rewrite files under a live game process | `src/Apocrypha.DataModel/Synchronizer/SynchronizerService.cs:305` |
| LOW | unverified | bug | InstallItem leaks its transaction when no installer succeeds | `src/Apocrypha.DataModel/LoadoutManager.cs:399` |
| LOW | unverified | mainta | linux-fork branch breaks after PRs that individually passed — no merge gate | `.github/workflows/pr-builds.yaml:5` |
| LOW | unverified | mainta | Dead and permanently-quarantined test code: an empty test project and flaky tests with no re-inclusion path | `tests/Games/Apocrypha.Games.AdvancedInstaller.UI.Tests/Startup.cs:1` |
| LOW | unverified | bug | GetInstallationTarget indexes an initially-empty collection — install clicks can silently fail (or throw) before targets load | `src/Apocrypha.App.UI/Pages/LibraryPage/LibraryViewModel.cs:1039` |
| LOW | unverified | perfor | ObservableList.ApplyChanges for keyed changesets is O(n) per Remove/Update and silently drops Updates whose Previous isn't found | `src/Apocrypha.UI.Sdk/Extensions/R3Extensions.cs:244` |
| LOW | unverified | bug | Update-selected canExecute uses ObserveCountChanged — stale when selection content changes but count doesn't | `src/Apocrypha.App.UI/Pages/LibraryPage/LibraryViewModel.cs:195` |
| LOW | unverified | perfor | View conflicts button runs a DuckDB cross-join/unnest query synchronously on the UI thread | `src/Apocrypha.App.UI/Pages/Sorting/FileConflicts/FileConflictsViewModel.cs:261` |
| LOW | unverified | perfor | TreeDataGrid cell recycle keys allocate strings and call string.Intern on every realize/recycle during scrolling | `src/Apocrypha.App.UI/Controls/TreeDataGrid/CustomElementFactory.cs:36` |
| INFO | unverified | extens | Adding a fourth mod source still costs a parallel ~2k-line plumbing stack | `src/Apocrypha.App.UI/Pages/LibraryPage/LibraryViewModel.cs:257` |
| INFO | unverified | archit | Nexus identity is baked into core IGameData while other sources use capability interfaces — two competing game-mapping patterns | `src/Apocrypha.Sdk/Games/IGameData.cs:30` |
| INFO | unverified | securi | API key stored in plaintext JSON, passed on the CLI command line, and typed into an unmasked dialog | `src/Apocrypha.Abstractions.ModIo/ModIoSettings.cs:24` |
| INFO | unverified | extens | Phase 1 gap catalog vs the Nexus and Thunderstore sources (roadmap inventory) | `src/Apocrypha.App.UI/Pages/ModIoDataProvider.cs:93` |
| INFO | unverified | perfor | ProxyConsole Serializer mixes blocking reads into the async IPC path and writes unframed length prefixes separately | `src/Apocrypha.ProxyConsole/Serializer.cs:118` |

### Completeness-pass additions

New findings from the "what did everyone miss?" sweep across seven previously-uncovered blind spots.
Full detail (with per-finding notes and the areas that came back clean) is in
[`CODE_REVIEW_VERIFICATION.md`](./CODE_REVIEW_VERIFICATION.md) §3.

| Sev | Status | Cat | Finding | Location |
|-----|--------|-----|---------|----------|
| HIGH | completeness | securi | FOMOD destination paths get no `..` sanitization → arbitrary file write outside the game dir (confirmed exploitable end-to-end → potential RCE) | `src/Apocrypha.Games.FOMOD/FomodXmlInstaller.cs:173` |
| HIGH | completeness | bug | Interactive guided-installer window pops up during an *unattended* collection install when a FOMOD mod's `choices` is null | `src/Apocrypha.Collections/InstallCollectionDownloadJob.cs:141` |
| HIGH | completeness | bug | Process-singleton FomodXmlInstaller delegates raced across `Parallel.ForEachAsync` collection installs — mod A applies mod B's preset/archive | `src/Apocrypha.Games.FOMOD/FomodXmlInstaller.cs:112` |
| HIGH | completeness | bug | EGS/GOG locators never set Platform → Windows-via-Wine install mislabeled native Linux → wrong exe/version resolved | `src/Apocrypha.Backend/Games/Locators/GOGLocator.cs:67` |
| HIGH | completeness | extens | No Heroic/Legendary EGS locator on Linux — Epic games installed via Heroic are undetectable | `src/Apocrypha.Backend/ServiceExtensions.cs:41` |
| HIGH | completeness | perfor | DiagnosticManager never evicts per-loadout observables — each loadout viewed leaks a hot pipeline (incl. web-API calls) re-running the full pass on every DB change | `src/Apocrypha.DataModel/Diagnostics/DiagnosticManager.cs:46` |
| MEDI | completeness | bug | FOMOD guided installer can't be cancelled — cancellation token dropped while a step is displayed | `src/Apocrypha.Games.FOMOD/CoreDelegates/UiDelegate.cs:148` |
| MEDI | completeness | bug | PresetGuidedInstaller throws IndexOutOfRange when the FOMOD presents more steps than the preset recorded (or empty []) | `src/Apocrypha.Games.FOMOD/CoreDelegates/PresetGuidedInstaller.cs:35` |
| MEDI | completeness | bug | Stubbed FOMOD condition/context delegates silently produce wrong install plans; GetCurrentGameVersion()="" throws on version-gated FOMODs | `src/Apocrypha.Games.FOMOD/CoreDelegates/ContextDelegates.cs:69` |
| MEDI | completeness | bug | FOMOD source file the path-fixer can't remap is silently skipped but the install still reports Success (silent partial install) | `src/Apocrypha.Games.FOMOD/FomodXmlInstaller.cs:238` |
| MEDI | completeness | bug | Fire-and-forget ContinueWith in the FOMOD UI delegate swallows exceptions and can hang the executor | `src/Apocrypha.Games.FOMOD/CoreDelegates/UiDelegate.cs:149` |
| MEDI | completeness | bug | FOMOD preset choices silently not honored on group/option name mismatch (name-only join, no validation) → required-file group left unselected | `src/Apocrypha.Games.FOMOD/CoreDelegates/PresetGuidedInstaller.cs:38` |
| MEDI | completeness | bug | FomodChoice.idx parsed but ignored → duplicate option names resolve to the wrong/multiple selections | `src/Apocrypha.Games.FOMOD/FomodOptions.cs:28` |
| MEDI | completeness | bug | FOMOD shared UiDelegates replaced by preset installer, never restored → later interactive installs reuse stale preset state | `src/Apocrypha.Games.FOMOD/FomodXmlInstaller.cs:107` |
| MEDI | completeness | bug | Wine-prefix EGS/GOG discovery only scans ~/.wine and $WINEPREFIX, missing Bottles/Lutris/Heroic per-game prefixes | `src/Apocrypha.Backend/Games/Locators/WinePrefixWrappingLocator.cs:29` |
| MEDI | completeness | perfor | Full sync tree built on every diagnostic pass even for games (Bannerlord, BG3) whose emitters never consume it | `src/Apocrypha.DataModel/Diagnostics/DiagnosticManager.cs:88` |
| MEDI | completeness | bug | Duplicate DiagnosticId(Bannerlord, 16) shared by two diagnostics that can co-occur → ID-keyed dedup/dismissal conflates them | `src/Apocrypha.Games.MountAndBlade2Bannerlord/Diagnostics/Diagnostics.cs:820` |
| MEDI | completeness | bug | Bannerlord launch load order includes disabled modules (no IsEnabled filter) — "disable" doesn't remove a mod from the forced list | `src/Apocrypha.Games.MountAndBlade2Bannerlord/Helpers.cs:19` |
| MEDI | completeness | bug | Bannerlord Modules/Multiplayer typo'd as a duplicate of BirthAndDeath → vanilla multiplayer files needlessly backed up on ingest | `src/Apocrypha.Games.MountAndBlade2Bannerlord/BannerlordLoadoutSynchronizer.cs:31` |
| MEDI | completeness | bug | BG3 .pak parser assumes one Stream.Read returns the full decompressed size → valid zlib/zstd paks falsely rejected as corrupt | `src/Apocrypha.Games.Larian/BaldursGate3/Utils/PakParsing/PakFileParser.cs:245` |
| MEDI | completeness | securi | Untrusted BG3 .pak header fields drive unvalidated allocations / Int32 overflow → OOM DoS (bypasses the InvalidDataException-only catch) | `src/Apocrypha.Games.Larian/BaldursGate3/Utils/PakParsing/PakFileParser.cs:128` |
| MEDI | completeness | bug | GC runs on a thread-pool task against a stale DB snapshot (file-store lock only); dedup TOCTOU can leave a live item pointing at a deleted archive | `src/Apocrypha.App.GarbageCollection.DataModel/RunGarbageCollector.cs:25` |
| MEDI | completeness | archit | GC correctness depends on a closed set of 3 referencing entity types, no fail-safe → any other hash-holder is permanently deleted | `src/Apocrypha.App.GarbageCollection.DataModel/DataStoreReferenceMarker.cs:22` |
| MEDI | completeness | bug | Single-instance election treats any live process owning the recorded PID as "main is running" (no identity check) → recycled PID wedges startup/CLI | `src/Apocrypha.SingleProcess/SyncFile.cs:52` |
| LOW | completeness | securi | Untrusted FOMOD ModuleConfig.xml handed to external XmlScript parser with no in-repo XXE/DTD control | `src/Apocrypha.Games.FOMOD/FomodXmlInstaller.cs:122` |
| LOW | completeness | mainta | FOMOD install errors surfaced as a bare System.Exception | `src/Apocrypha.Games.FOMOD/FomodXmlInstaller.cs:131` |
| LOW | completeness | bug | MissingMasterEmitter's legacy Diagnose overload throws NotImplementedException — landmine for pre-sync-tree callers | `src/Apocrypha.Games.CreationEngine/Emitters/MissingMasterEmitter.cs:25` |
| LOW | completeness | mainta | Static lock guards instance-level DiagnosticManager caches | `src/Apocrypha.DataModel/Diagnostics/DiagnosticManager.cs:24` |
| LOW | completeness | securi | Bannerlord SubModule.xml parsed with DtdProcessing enabled → billion-laughs entity-expansion DoS | `src/Apocrypha.Games.MountAndBlade2Bannerlord/Installers/BannerlordModInstaller.cs:192` |
| LOW | completeness | bug | BG3 .pak FileListOffset (UInt64) truncated to int → parse corruption for paks larger than 2GB | `src/Apocrypha.Games.Larian/BaldursGate3/Utils/PakParsing/PakFileParser.cs:27` |
| LOW | completeness | bug | BG3 meta.lsx located by unanchored substring Contains → can select the wrong file → wrong dependency diagnostics | `src/Apocrypha.Games.Larian/BaldursGate3/Utils/PakParsing/PakFileParser.cs:30` |
| LOW | completeness | securi | MultiProcessSharedArray bounds checks compiled out in Release → latent OOB native write (not currently reachable) | `src/Apocrypha.SingleProcess/MultiprocessSharedArray.cs:107` |
| INFO | completeness | mainta | Swapped operands in BG3 .pak file-list decompression-mismatch error message | `src/Apocrypha.Games.Larian/BaldursGate3/Utils/PakParsing/PakFileParser.cs:148` |

---

*Generated by a 14-agent deep code review with adversarial verification, then completed by a
verification pass (139 findings adversarially verified: 99 confirmed real, 3 refuted) and a
7-sweep completeness critic that added ~33 new findings (7 high, incl. a confirmed FOMOD
arbitrary-file-write). Reviewed against `linux-fork` at commit `80037f4`. Verification record:
[`CODE_REVIEW_VERIFICATION.md`](./CODE_REVIEW_VERIFICATION.md).*
