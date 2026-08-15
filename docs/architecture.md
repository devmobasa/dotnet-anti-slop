# Architecture

## Why three layers

A text-only rule set is easy to adopt but cannot distinguish a real EF Core
`FromSqlRaw` call from a same-named helper, a real minimal API mapping from an
application method named `MapGet`, or an entity projection from a scalar
projection. A Roslyn-only package, meanwhile, cannot encode design questions
such as whether query splitting or pooling is appropriate.

The repository therefore separates:

1. **shared engineering guidance** (`AGENTS.md`, editor integrations);
2. **automated policy** (SDK analyzers plus `DASxxxx`);
3. **review-only guidance** (`docs/best-practices.md`).

## Analyzer organization

- `RuntimeAnalyzer` owns C#, LINQ, allocation, async, and fan-out rules.
- `AspNetCoreAnalyzer` owns service registration and request-lifetime rules.
- `EfCoreAnalyzer` owns query-chain and `DbContext` rules.
- `TestingAnalyzer` owns test-runner semantics.

All analyzers:

- target `netstandard2.0`;
- use symbol resolution before framework-specific reporting;
- disable generated-code analysis;
- enable concurrent execution;
- expose stable IDs through `DiagnosticIds`;
- are configured by `.globalconfig`, not hard-coded project assumptions.

## Semantic boundaries

Framework matching requires both method/type name and resolved namespace/type.
The EF rules trace a receiver to `DbSet<TEntity>` and compare the materialized
element type with the entity type. The route rules inspect the resolved
ASP.NET Core mapping method and HTTP method. DI lifetime analysis collects
registrations for one compilation and compares singleton constructor
dependencies with scoped registrations.

The implementation remains deliberately local. It does not attempt whole-
solution points-to analysis, runtime configuration evaluation, database schema
inspection, or cross-process tracing.

## Distribution

### Vendored

The installer copies analyzer source into `.dotnet-anti-slop/analyzer`, copies
the selected profile, writes local props and targets files, and adds managed
imports to the consumer's `Directory.Build.props` and
`Directory.Build.targets`.

The vendored project carries its own `Directory.Build.props` and explicitly
sets `ManagePackageVersionsCentrally=false`. This prevents the host
repository's `Directory.Packages.props` from forcing a different Roslyn package
version or producing central-package-management errors.

### NuGet

The analyzer DLL is packed under `analyzers/dotnet/cs`. Profiles and a
`buildTransitive` props file select `default` unless
`DotNetAntiSlopProfile` is set by the consumer.

## Compatibility

Roslyn 4.8 is the API floor because it ships with the .NET 8 generation and
provides a broad stable host base. Newer SDKs load the same analyzer assembly.
CI builds one analyzer binary with the repository's pinned .NET 10 SDK.
