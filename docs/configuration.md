# Configuration

## Select a profile

NuGet installation:

```xml
<PropertyGroup>
  <DotNetAntiSlopProfile>default</DotNetAntiSlopProfile>
</PropertyGroup>
```

Accepted values: `default`, `strict`, `performance`, `web-api`.

Vendored installation stores the selected profile at
`.dotnet-anti-slop/config/<profile>.globalconfig`.

## Override a diagnostic

In the nearest `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.DAS3008.severity = warning

[tests/**/*.cs]
dotnet_diagnostic.DAS1012.severity = suggestion
```

Valid values are `error`, `warning`, `suggestion`, `silent`, and `none`.

## Baseline a legacy repository

Do not merge a giant blanket suppression. Use staged adoption:

1. install with the `default` profile;
2. export current diagnostics from CI/build logs;
3. fix errors and high-confidence warning classes;
4. temporarily lower selected medium-confidence IDs in legacy folders;
5. enforce the full profile on new/changed projects;
6. delete folder exceptions as debt is removed.

A baseline should have an owner and removal condition.

## Generated code

The custom analyzers opt out of generated code. Keep generated files marked by
the generator and do not suppress diagnostics in hand-written partial members
that happen to sit beside generated code.

## Treat warnings as errors

This repository does. A consumer can apply:

```xml
<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

For gradual rollout, use `WarningsAsErrors` with selected IDs instead of
turning every existing compiler warning into an immediate migration blocker.

## Suppression

Narrow pragma:

```csharp
#pragma warning disable DAS3008 // Domain guarantees at most 12 ISO currencies.
var currencies = await db.Currencies
    .AsNoTracking()
    .ToListAsync(cancellationToken);
#pragma warning restore DAS3008
```

An architectural exception should explain the bound or ownership—not merely
say “false positive.”
