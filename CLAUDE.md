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
`NEXUS_API_KEY` is unset. The network-bound remainder runs in
`.gitea/workflows/nightly-networking.yaml` (scheduled + manual dispatch, never PR-blocking,
`NEXUS_API_KEY` from a Gitea repo secret, `CollectionInstallTests` excluded as CI-hostile) —
a red there means "Nexus-side change or outage" until proven otherwise, not a broken PR.

### CI verification gotchas

- A merge is not verified until the **post-merge push run** on `linux-fork` is green:
  `tea actions runs view <id>` and read `Conclusion`. Both `tea actions runs list` and the
  UI say "completed" for passes AND failures — run 125 (#14's merge push) failed silently
  this way and nobody noticed until the next session.
- CI checkout failures of the form `Failed to connect to gitea-ec2.… port 443` are the
  Tailscale **exit-node/Docker trap** on the runner host, not a broken PR: an active exit
  node kills egress from job containers on bridge networks. Durable fix is
  `container.network: host` in `/etc/gitea-runner/config.yaml`; interim is exit node off,
  rerun. Check `tailscale status | grep 'exit node;'` before diagnosing anything else.
- To exercise CI without a PR: `curl -X POST .../actions/workflows/ci.yaml/dispatches
  -d '{"ref":"<branch>"}'` with an API token (`tea api` cannot send request bodies).

### Tests are mixed-framework

Newer suites (e.g. `Apocrypha.Backend.Tests`) are **TUnit** (`[Test]`, `[Arguments]`,
`await Assert.That(...)`); older ones are xunit + FluentAssertions. Check the csproj's
`PackageReference`s before writing tests in an unfamiliar project.

## Branch and PR rules

- `linux-fork` is the default branch and is **protected**: PR-only, no direct commits,
  no force-push. Merges and release publishing are the maintainer's call — open the PR
  and stop.
- Branch naming: `feat/...`, `fix/...`, `docs/...`.
- Zero-warnings policy: the build is warning-clean; keep it that way.

## Outside contributions

**Never merge an outside PR on GitHub.** Replay it onto Gitea, merge it there, and let the push
mirror carry the result back.

Contributors can only reach GitHub — Gitea is Tailscale-only and never publicly exposed — but
GitHub is a **push-mirror target**, force-pushed from Gitea on every commit. That works only
while GitHub exclusively *receives*. Merging on GitHub gives `linux-fork` two writers: the
histories diverge, GitHub's branch protection refuses the mirror's force-push, and the mirror
**silently stops updating** while Gitea moves on. Since releases are cut from GitHub's
`linux-fork`, a release cut during that window ships the wrong tree.

Replay = cherry-pick their commit onto Gitea's head (this preserves the `Author:` line and their
contribution credit), prove it faithful with `git diff github/linux-fork HEAD -- <files>`, then
PR it on Gitea. **Verify it yourself**: GitHub runs no CI, so an outside commit has never been
built or tested by our pipeline — run the suite and independently falsify their tests rather
than trusting the commit message. Leave their commit message verbatim, trailers included.

Check mirror health when opening a PR and after every merge:

```sh
git log --oneline origin/linux-fork..github/linux-fork   # non-empty = GitHub has unmirrored commits
tea api /repos/Jeagermeister/Apocrypha/push_mirrors      # read last_error
```

A non-empty `last_error` (`GH006 … Cannot force-push to this branch`) means the mirror is stuck.
An *empty* one is not proof of health — `last_update: 1970-…` means it never synced.

Keep GitHub's branch protection **on**: it is what turns silent data loss into a loud,
recoverable error. Note `allow_force_pushes: false` is not bypassed by admin status, and a Gitea
**web-UI merge** fires the mirror server-side, so no local `pre-push` hook can guard this. A
merged PR's commits survive at `refs/pull/<N>/head` on GitHub permanently.

Full procedure, including how to un-stick a diverged mirror: `apocrypha-contributions` skill.

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
