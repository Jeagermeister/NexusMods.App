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

CI (`pr-builds.yaml`) builds Linux + Windows (macOS is intentionally unsupported).
Networking, mod-install, and clean-environment suites run as separate CI jobs with
`dotnet test --filter ...`; don't be surprised when they're skipped locally.

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
produces 4 artifacts (AppImage, Linux zip, InnoSetup `Apocrypha.x64.exe`, Windows zip)
as a draft — the maintainer titles and publishes. The binary version is derived from
the tag; keep them matching so the in-app updater behaves.
