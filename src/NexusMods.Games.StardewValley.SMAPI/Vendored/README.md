# Vendored SMAPI Toolkit

This directory contains vendored source from [Pathoschild/SMAPI](https://github.com/Pathoschild/SMAPI),
licensed under LGPL-3.0 (see `LICENSE.txt` in this directory).

- **Vendored trees**: `src/SMAPI.Toolkit` and `src/SMAPI.Toolkit.CoreInterfaces`
- **Source commit**: `fd734460` (branch `stable`, 2024-12-18) — the commit the
  old `extern/SMAPI` submodule was pinned to when it was replaced by this copy.
- **Local patches**: marked with `// Apocrypha:` comments. Currently:
  - `SMAPI.Toolkit/Framework/LowLevelEnvironmentUtility.cs`: suppressed
    SYSLIB0037 (upstream reads the obsolete `AssemblyName.ProcessorArchitecture`).

## Updating

Diff these trees against the same paths in the SMAPI repository at the new
commit, re-apply the `// Apocrypha:` patches, and update the package versions
in `NexusMods.Games.StardewValley.SMAPI.csproj` to match SMAPI's
`src/Directory.Packages.props` for `HtmlAgilityPack`, `Markdig`,
`Newtonsoft.Json`, and `Pathoschild.Http.FluentClient`.
