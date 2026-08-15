---
name: install-dotnet-anti-slop
description: Install, refresh, inspect, or remove the self-contained dotnet-anti-slop Roslyn analyzer policy in a target .NET repository. Use when adopting semantic DAS diagnostics, choosing a default, strict, performance, or web-api profile, migrating an existing vendored copy, or validating analyzer installation without cloning the policy repository separately.
---

# Install dotnet-anti-slop

Install the synchronized analyzer assets with the deterministic script while
adapting the change to the target repository's existing build policy.

## Inspect before changing

1. Read every applicable repository instruction file.
2. Inspect `git status`, including untracked files. Preserve unrelated work.
3. Find solutions/projects, `global.json`, `Directory.Build.*`,
   `Directory.Packages.props`, existing analyzers, and CI build commands.
4. Inspect an existing `.dotnet-anti-slop` directory and compare local changes
   before using `--force`.
5. Select `default` unless project evidence supports `strict`, `performance`,
   or `web-api`.

Do not overwrite or merge agent guidance automatically. Ask whether
`assets/agent-guidance.md` should be linked, merged into existing instructions,
or left separate when the user has not already decided.

## Install

Run:

```bash
./scripts/install.sh /absolute/path/to/target --profile default
```

On Windows PowerShell, run:

```powershell
./scripts/install.ps1 -TargetPath C:\src\target -Profile default
```

Use `--dry-run` first when the target has custom MSBuild imports. Use `--force`
only after comparing the current vendored copy. Use `--uninstall` to remove the
managed imports and vendored directory.

The script copies `assets/analyzer` and the selected synchronized profile. It
preserves unrelated `Directory.Build.props` and `Directory.Build.targets`
content and isolates the analyzer from central package management.

## Validate the target

1. Review the complete diff and confirm only the intended import and vendored
   files changed.
2. Run the target repository's normal restore, warning-free build, and tests
   under its selected SDK.
3. If practical, compile one known DAS violation and confirm the expected
   diagnostic, then remove the probe.
4. Report the profile, source revision from `INSTALLATION.md`, changed paths,
   selected SDK, commands run, and remaining diagnostics.

Treat `dirty` or `modified-skill` source state as an explicit provenance
warning. Use the recorded content SHA-256 to distinguish the copied snapshot
from its base revision.

Never commit or publish unless the user explicitly asks.
