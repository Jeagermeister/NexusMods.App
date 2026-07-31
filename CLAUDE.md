# CLAUDE.md

Apocrypha — a Linux-first mod manager, hard-forked from NexusMods.App. C# / .NET
(`net9.0`), Avalonia UI, MnemonicDB (RocksDB-backed) datastore. All namespaces and
project names are `Apocrypha.*`; the solution is `Apocrypha.sln`.

## Commands

```sh
dotnet build                                    # full solution build
dotnet run --project src/Apocrypha.App          # run the app
dotnet test                                     # default test suite
```

**CI runs on self-hosted Gitea, not GitHub** (moved 2026-07-30). The pipeline is
`.gitea/workflows/ci.yaml` — one job, build + test on every PR and push to `linux-fork`,
executed by `act_runner` on Brian's own machines. GitHub deliberately runs **nothing**; it
is a push-and-release mirror. `.github/workflows/` retains only release machinery
(`release.yaml` and the pupnet builders), and migrating that to Gitea is still open work.

Beware: **Gitea Actions reads `.github/workflows` as well as `.gitea/workflows`.** Any CI
workflow left under `.github/` will run on Gitea too — that is why the GitHub CI and
maintenance workflows were deleted rather than merely disabled.

CI builds **Linux only**. Windows was deliberately removed from CI, releases and tests — it
was failing on races that do not affect the shipped product, and Apocrypha is Linux-first.
Windows comes back when we choose to support it properly; the reusable
`build-windows-pupnet.yaml` workflow is kept (unreferenced) so re-enabling is a small
change. macOS is intentionally unsupported.

The test filter is `RequiresNetworking!=True&FlakeyTest!=True`, so network-bound suites are
skipped in CI and locally alike — don't be surprised by the ~64 local failures when
`NEXUS_API_KEY` is unset.

## Branch and PR rules

- `linux-fork` is the default branch and is **protected**: PR-only, no direct commits,
  no force-push. Merges and release publishing are the maintainer's call — open the PR
  and stop.
- Branch naming: `feat/...`, `fix/...`, `docs/...`.
- Zero-warnings policy: the build is warning-clean; keep it that way.

## Hard constraints

- **Schema stability**: MnemonicDB attribute ids and `JsonName` discriminators are
  persisted in users' datastores. Renaming or moving them breaks existing installs —
  a schema-fingerprint test guards this. Never rename persisted identifiers as part of
  a refactor; if a schema change is intentional, the fingerprint must be consciously
  re-accepted.
- **Test isolation**: tests that boot the app must isolate `XDG_RUNTIME_DIR` and
  `XDG_CONFIG_HOME` (not just `HOME`), or they will clobber the developer's real
  desktop-file protocol handlers and OAuth login.
- A stale single-process sync file (`$XDG_RUNTIME_DIR/Apocrypha-sync_file.sync`) left
  behind by a force-killed instance hangs subsequent CLI/app runs — delete it. A RocksDB
  LOCK error means another instance is still alive.

## Releases

Dispatched via `release.yaml` (workflow_dispatch, `version: vX.Y.Z`) off `linux-fork`;
produces 2 artifacts (AppImage, Linux zip)
as a draft — the maintainer titles and publishes. The binary version is derived from
the tag; keep them matching so the in-app updater behaves.
