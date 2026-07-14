# Code Review — Fixes Applied (Tier 0 + Tier 1)

*Companion to [`CODE_REVIEW.md`](./CODE_REVIEW.md) and
[`CODE_REVIEW_VERIFICATION.md`](./CODE_REVIEW_VERIFICATION.md). This is the record of the code fixes
made against the review's roadmap, with per-fix verification steps.*

> **⚠️ Not compiled/tested in the authoring environment.** These changes were written in a sandbox
> with **no .NET SDK** (the SDK download is blocked by the environment proxy), so nothing here has
> been compiled or run. Each fix was made against the actual source with careful review; algorithmic
> logic (the FOMOD sanitizer) was validated with a standalone port. **Please build + run the tests
> locally before merging.** One item (the migration) is intentionally left un-registered pending a
> local legacy-DB test — see #6.

Each fix is its own commit for easy review/cherry-pick.

---

## Summary

| # | Fix | Files | Risk | What to run locally |
|---|-----|-------|------|---------------------|
| 0 | 🔴 FOMOD path-traversal (arbitrary file write) | `Apocrypha.Games.FOMOD/FomodXmlInstaller.cs` (+test) | Low | `Apocrypha.Games.FOMOD.Tests` (new `Test_SanitizeDestination_StripsTraversal`) |
| 1 | `SortOrderManager` singleton clobber | `Apocrypha.Abstractions.Games/Services.cs` | Low | Open two games' load-order UIs in one session; both keep their varieties |
| 2 | `CopyLoadout` loses load order + priorities | `Apocrypha.DataModel/LoadoutManager.cs` (+test) | Med | `GeneralLoadoutManagementTests.CopyLoadout_CopiesLoadOrder` |
| 3 | HTTP decompression off | `Apocrypha.Networking.HttpDownloader/Services.cs` | Trivial | Any Thunderstore modpack resolve (watch bytes/latency) |
| 4 | Download resume corruption | `Apocrypha.Networking.HttpDownloader/HttpDownloadJob.cs` | Med | `Apocrypha.Networking.HttpDownloader.Tests` + a no-range-server resume |
| 5 | Nondeterministic `plugins.txt` | `Apocrypha.Games.CreationEngine/PluginsFile.cs` | Low | Apply a Skyrim/FO4 loadout twice; `plugins.txt` is byte-identical |
| 6 | Migration `_0009` broken query | `.../Migrations/_0009_*.cs`, `.../SchemaVersions/Services.cs` | **Gated** | Add legacy-DB test, then register (see below) |
| 7 | `NxFileStore.BackupFiles` fd exhaustion | `Apocrypha.DataModel/NxFileStore.cs` | Med | Back up an unknown-version / many-file game; no fd crash |

---

## 0 — FOMOD path traversal → arbitrary file write (security, Tier 0)

**Bug:** a mod's untrusted `ModuleConfig.xml` destination (e.g. `..\..\..\evil.dll`) was only
leading-slash-trimmed; `RelativePath.FromUnsanitizedInput` doesn't strip `..`, and every downstream
hop is pure concatenation, so the OS resolved the `..` and wrote attacker bytes **outside the game
directory** on apply. Confirmed exploitable end-to-end in the completeness pass.

**Fix:** new `SanitizeDestination()` drops root / empty / dot-and-space-only (`.`/`..`) path
segments so the result stays contained (mirrors `ManagedZipExtractor` + `PathsHelper.FixPath`).
Applied at both sinks — `FixPaths` and `ConvertInstructionCopy` (the write path), with a filename
fallback if a destination sanitizes away entirely.

**Verify:** run `Apocrypha.Games.FOMOD.Tests`. New theory `Test_SanitizeDestination_StripsTraversal`
asserts benign paths are preserved and every traversal payload is neutralized (no `..` survives).
The sanitizer's algorithm was validated against all its test cases via a standalone port.

## 1 — SortOrderManager singleton clobber (load order, Tier 1)

**Bug:** the manager was a process-wide **singleton**, but every game's `game.SortOrderManager`
resolves that one instance and calls `RegisterSortOrderVarieties`, which **replaces** the variety
dictionary. Opening any game after Cyberpunk wiped Cyberpunk's REDmod varieties for the session (and
leaked the change subscription).

**Fix:** register `SortOrderManager` as **transient**. Each game's `Lazy<ISortOrderManager>` resolves
and caches its own instance with its own varieties + subscription — no shared state, no interface
change. Consumers already access it only via `game.SortOrderManager`.

**Verify:** in a multi-game session, open Cyberpunk's load-order (Rules) UI, then another game's page,
then return to Cyberpunk — the REDmod load order should still be present and still update on add/remove.

## 2 — CopyLoadout drops load order + conflict priorities (Tier 1)

**Bug:** `CopyLoadout` only remapped `Loadout` + `LoadoutItem` entities. `SortOrder`,
`SortOrderItem`, and `LoadoutItemGroupPriority` are keyed to the loadout, not to items, so they were
never copied — the clone lost its REDmod order and, with zero priority rows, resolved conflicts by
nondeterministic tie-break (could deploy different winning files than the original).

**Fix:** add those entities to the old→new id map so the existing generic datom-copy loop remaps
their references onto the clone. Game-specific extensions (`RedModSortOrder/Item`) share the base
entity id, so their datoms copy too. Mirrors `CloneCollection`.

**Verify:** `GeneralLoadoutManagementTests.CopyLoadout_CopiesLoadOrder` (new) asserts the clone gets
its own `SortOrder` referencing the clone. Manually: hand-tune a Cyberpunk REDmod order + some
conflict priorities, clone the loadout, confirm the clone matches.

## 3 — HTTP responses transferred uncompressed (perf, Tier 1)

**Fix:** set `AutomaticDecompression = DecompressionMethods.All` on the shared `SocketsHttpHandler`.
The multi-MB Thunderstore v1 index and all Nexus/GraphQL JSON now negotiate + transparently
decompress gzip/deflate/brotli.

**Verify:** resolve a large Thunderstore modpack (Lethal Company / RoR2) and compare bytes-on-wire /
latency before vs after; JSON traffic should also shrink.

## 4 — Download corruption on resume against a no-range server (Tier 1)

**Bug:** the stream is seeked to the resume offset, but the reset-to-zero guard only fired for
`isRangeRequest && 200`. If the server doesn't support ranges and we retry after partial progress, a
plain GET returns the full body which was then written at the non-zero offset → stale prefix +
full body = corrupt archive.

**Fix:** reset (position 0 + truncate) whenever a **200** arrives while bytes are already written —
covering both the ignored-range and no-range-support cases.

**Verify:** `Apocrypha.Networking.HttpDownloader.Tests`. Ideally add a test with a `LocalHttpServer`
that ignores `Range` and returns 200 mid-resume; assert the output equals the source bytes.

## 5 — Nondeterministic `plugins.txt` (Creation Engine, Tier 1)

**Bug:** the `Sorter` was called with no tie-break comparer, so same-tier plugins were emitted in
hash-randomized dictionary order — a different `plugins.txt` on every launch.

**Fix:** pass a deterministic `ModKey` comparer (ordinal-ignore-case by filename). It only breaks
ties within an eligible batch; the ESM/ESL/ESP + master-reference rules still dominate.

**Verify:** apply the same Skyrim/FO4 loadout across two separate app runs and diff `plugins.txt` —
it should be byte-identical. (Before, it varied run-to-run with no user action.)

## 6 — Migration `_0009` conflict-priority backfill (Tier 1) — **registration gated**

**Bug:** `_0009` was unregistered **and** its query referenced a nonexistent
`loadouts.ItemGroupEnabledState` macro. Legacy loadouts therefore have no priority rows (every
conflict ties at 0, invisible to the conflicts UI).

**Fix (partial, deliberately):** the query is rewritten to use only generated `MDB_*` table macros
(join `LoadoutItemGroup`→`LoadoutItem` on the shared id to filter by loadout, select groups lacking a
priority). The `Migrate` step already matches `AddPriorityTxFunc` semantics.

**NOT registered yet — on purpose.** A migration runs against real user DBs at startup, and this
environment couldn't execute the SQL to verify it. `Services.cs` documents the remaining step:

1. Add a legacy-DB test beside the other `MigrationSpecificTests` — migrate a Migration-8 snapshot
   and assert every `LoadoutItemGroup` gets a sequential priority.
2. Once green, append `.AddMigration<_0009_AddLoadoutItemGroupPriority>()` to the chain in
   `Services.cs`.

## 7 — NxFileStore.BackupFiles fd exhaustion (Tier 1)

**Bug:** every source stream was opened at once and held until `NxPackerBuilder.Build()` consumed
them, so a many-file mod / unknown-version game (100k+ files) blew past the Linux 1024-fd limit and
crashed mid-manage; a thrown exception also leaked the streams.

**Fix:** pack in batches of 512 (each its own `.nx` archive — the store already maps each hash to its
archive) and dispose each batch's streams in a `finally`. Partial failure is safe to retry (the
existing dedup check skips already-backed-up files).

**Verify:** manage a game with a very large file count (or an unknown-version game whose whole install
lands in overrides) and confirm it completes without an fd-exhaustion crash; multiple `.nx` archives
in the store are expected and fine.

---

## What's next (from the roadmap, not yet done)

Tier 2+ remain: the fork-owned hash-DB update feed (§7 #8), the `IModSource` interface set before
Modrinth (#9), plugin `Sorter` memoization (#10), post-sync GC debounce + diff-based loadout
switching (#11), the effector test suite (#12–13), and the security hardening (#14–15). The
completeness pass also surfaced high-value follow-ups (Tier 5): the FOMOD-in-collection hardening,
the Linux Epic/GOG locator fixes, and the diagnostics observable-eviction leak. See
`CODE_REVIEW.md` §7 and `CODE_REVIEW_VERIFICATION.md` §3.
