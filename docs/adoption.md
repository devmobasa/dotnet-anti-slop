# Adoption guide

## 1. Choose enforcement mode

Use vendoring when teams want source visibility, local rule changes, or a
single-repository rollout without publishing infrastructure. Use NuGet when a
platform team centrally publishes signed/versioned policy.

Do not copy only `AGENTS.md` and assume enforcement exists. Agent instructions
shape generation; analyzers verify compiled code.

## 2. Run the installer

```bash
./eng/install.sh --profile default ../YourRepository
```

The installer creates:

```text
.dotnet-anti-slop/
  analyzer/
  config/<profile>.globalconfig
  DotNetAntiSlop.props
  DotNetAntiSlop.targets
```

and adds marked import blocks to `Directory.Build.props` and
`Directory.Build.targets`.

Use `--force` to refresh an existing vendored copy after reviewing upstream
changes. Use `--uninstall` to remove the import and vendored directory.

## 3. Establish a baseline

Build each solution and classify findings:

- correctness/security/lifetime: fix before rollout;
- scalable query/request bounds: fix public/high-volume paths first;
- measured performance: compare telemetry and choose severity;
- intentional exception: suppress narrowly with evidence.

Track diagnostic counts in CI, but avoid a metric that rewards hiding warnings.

## 4. Add CI

Run the integrity checker and normal build/test. Analyzer diagnostics naturally
flow through `dotnet build`.

For a shared analyzer repository, keep an oldest-supported SDK lane and a
newest/preview lane. For an application repository, use the SDKs it actually
ships, plus preview only when early compatibility matters.

## 5. Tune by code area

Examples:

```ini
# New API projects are strict.
[src/Api/**/*.cs]
dotnet_diagnostic.DAS3008.severity = warning
dotnet_diagnostic.DAS2006.severity = error

# A migration tool has deliberate synchronous boundaries.
[tools/Migrator/**/*.cs]
dotnet_diagnostic.DAS1001.severity = suggestion
```

Do not lower SQL parameterization, invalid context lifetime, or async-void test
findings without replacing them with an equivalent control.

## 6. Operationalize review guidance

Static analysis cannot check database plans, endpoint cardinality, cache
invalidation, or retry idempotence. Add pull-request templates and architecture
checks from `docs/review-checklists/`.

## 7. Upgrade

For vendored mode, compare upstream release notes and run the installer with
`--force`. For NuGet, update through normal package governance. Keep rule
severities explicit so a package update cannot silently change CI policy.
