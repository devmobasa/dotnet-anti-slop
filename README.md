# dotnet-anti-slop

A policy toolkit for **C#, .NET, ASP.NET Core, and EF Core** inspired by
[dmmulroy/anti-slop](https://github.com/dmmulroy/anti-slop). It combines shared
guidance for people and coding assistants with semantic Roslyn diagnostics,
SDK analyzer configuration, adoption scripts, tests, and framework review
playbooks.

The analyzer assembly targets `netstandard2.0` and references Roslyn 4.8 APIs,
so it does not require applications to target a particular runtime. Repository
development and CI use the .NET 10 SDK.

## What it catches

The pre-release analyzer set contains **33 diagnostics**:

- allocation and collection mistakes, including string accumulation in loops,
  avoidable boxing, repeated enumeration, and missing capacity;
- async and concurrency mistakes, including sync-over-async, `async void`,
  dropped cancellation, inline `TaskCompletionSource` continuations,
  `ValueTask` double consumption, and unbounded fan-out;
- ASP.NET Core mistakes, including nested service providers, invalid DI
  lifetimes, per-request `HttpClient`, unguarded development middleware, and
  captured `HttpContext` values and request fire-and-forget work;
- EF Core mistakes, including read-only tracked queries, missing cancellation,
  N+1 query loops, client-side shaping, dynamic raw SQL, unbounded endpoint
  materialization, `CountAsync` existence checks, and concurrent use of one
  `DbContext`;
- asynchronous test methods returning `void`.

The rules are deliberately contextual. For example, `DAS3001` does **not**
demand `AsNoTracking()` for DTO/scalar projections, and no rule says that every
`Task` should become `ValueTask`. Advice that needs measurement or domain
context lives in the review playbooks instead of producing noisy warnings.

## Quick start

Choose the installation path that fits your repository. The agent skill and
manual installer both vendor the same inspectable analyzer source and profile.

### Install with an agent

Install the self-contained repository-aware skill, then ask the agent to apply
it to the target .NET repository:

```bash
npx skills add devmobasa/dotnet-anti-slop \
  --skill install-dotnet-anti-slop
```

The skill inspects existing repository instructions, build policy, central
package management, SDK selection, and dirty state before invoking the
deterministic vendored installer. Canonical analyzer and profile assets are
kept byte-for-byte synchronized with `./eng/sync_skill_assets.sh --check`.

### Install manually from a checkout

This keeps the policy source inspectable and lets a team change or extend it.

```bash
# From this dotnet-anti-slop checkout:
./eng/install.sh --profile web-api /path/to/your/solution
```

PowerShell:

```powershell
./eng/install.ps1 -Profile web-api -TargetPath C:\src\YourSolution
```

The Unix installer is a native shell script and the Windows installer is
native PowerShell; neither needs a separate scripting runtime.

The installer creates an isolated `.dotnet-anti-slop/` directory and adds one
managed import each to the target repository's `Directory.Build.props` and
`Directory.Build.targets`. A forced refresh replaces those managed blocks
without duplicating them. The vendored analyzer explicitly opts out of the
consumer's central package management so its Roslyn dependency cannot collide
with application package versions.

### Install the NuGet package

```bash
dotnet add YourProject.csproj package DotNetAntiSlop.Analyzers \
  --version 0.1.0-preview.1
```

To test an unreleased checkout locally, build the package and use its output as
an explicit source:

```bash
dotnet pack src/DotNetAntiSlop.Analyzers/DotNetAntiSlop.Analyzers.csproj \
  -c Release -o artifacts/packages
dotnet add YourProject.csproj package DotNetAntiSlop.Analyzers \
  --source artifacts/packages --version 0.1.0-preview.1
```

Select a package profile in `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <DotNetAntiSlopProfile>web-api</DotNetAntiSlopProfile>
  </PropertyGroup>
</Project>
```

Available profiles are `default`, `strict`, `performance`, and `web-api`.

## Example

```csharp
// DAS3001 + DAS3008: tracked and unbounded read path.
app.MapGet("/orders", async (AppDbContext db, CancellationToken ct) =>
    await db.Orders.ToListAsync(ct));

// Bounded, projected, cancellable, and no-tracking.
app.MapGet("/orders", async (
    int? afterId,
    AppDbContext db,
    CancellationToken ct) =>
{
    var query = db.Orders
        .AsNoTracking()
        .OrderBy(order => order.Id);

    if (afterId is { } cursor)
    {
        query = query.Where(order => order.Id > cursor)
            .OrderBy(order => order.Id);
    }

    return await query
        .Take(100)
        .Select(order => new OrderSummary(order.Id, order.Number))
        .ToListAsync(ct);
});
```

## Design

The repository has three enforcement layers:

1. **Compiler and SDK policy.** `.globalconfig` profiles turn on proven `CAxxxx`
   and `ASPxxxx` diagnostics and resolve known duplicate findings.
2. **Semantic Roslyn diagnostics.** `DASxxxx` rules resolve symbols, receiver
   types, query chains, endpoint mappings, attributes, and DI registrations.
   They are not grep rules.
3. **Review playbooks.** Compiled queries, `ValueTask` return types,
   `AddDbContextPool`, split queries, pooling, spans, and other workload-specific
   choices require profiling or architectural context and are not universal
   diagnostics.

## Rule catalog

| ID | Area | Rule | Default |
|---|---|---|---|
| [DAS1001](docs/rules/DAS1001.md) | Async | Avoid sync-over-async | warning |
| [DAS1002](docs/rules/DAS1002.md) | Async | Do not call Thread.Sleep from asynchronous code | warning |
| [DAS1003](docs/rules/DAS1003.md) | Async | Avoid async void outside event handlers | error |
| [DAS1004](docs/rules/DAS1004.md) | Async | Forward the available CancellationToken | warning |
| [DAS1005](docs/rules/DAS1005.md) | Async | Do not replace an available token with CancellationToken.None | warning |
| [DAS1006](docs/rules/DAS1006.md) | Performance | Avoid string accumulation in loops | warning |
| [DAS1007](docs/rules/DAS1007.md) | LINQ | Use Any for an existence check | warning |
| [DAS1008](docs/rules/DAS1008.md) | LINQ | Avoid repeated enumeration of a lazy sequence | warning |
| [DAS1009](docs/rules/DAS1009.md) | Collections | Set collection capacity when the size is knowable | suggestion |
| [DAS1010](docs/rules/DAS1010.md) | Performance | Avoid boxing in hot loops | warning |
| [DAS1011](docs/rules/DAS1011.md) | Async | Consume a ValueTask only once | error |
| [DAS1012](docs/rules/DAS1012.md) | Concurrency | Bound asynchronous fan-out | warning |
| [DAS1013](docs/rules/DAS1013.md) | Async | Run TaskCompletionSource continuations asynchronously | warning |
| [DAS1014](docs/rules/DAS1014.md) | Async | Do not convert async lambdas to void-returning delegates | error |
| [DAS2001](docs/rules/DAS2001.md) | ASP.NET Core | Do not build a nested service provider | error |
| [DAS2002](docs/rules/DAS2002.md) | Dependency Injection | Do not register DbContext as a singleton | error |
| [DAS2003](docs/rules/DAS2003.md) | Dependency Injection | Do not capture a scoped dependency in a singleton | error |
| [DAS2004](docs/rules/DAS2004.md) | ASP.NET Core | Request handlers should accept CancellationToken | warning |
| [DAS2005](docs/rules/DAS2005.md) | HTTP | Do not construct HttpClient per request | warning |
| [DAS2006](docs/rules/DAS2006.md) | ASP.NET Core | Guard development-only middleware | warning |
| [DAS2007](docs/rules/DAS2007.md) | ASP.NET Core | Do not fire-and-forget request work | error |
| [DAS2008](docs/rules/DAS2008.md) | ASP.NET Core | Do not cache IHttpContextAccessor.HttpContext | warning |
| [DAS3001](docs/rules/DAS3001.md) | EF Core | Use no-tracking for read-only entity queries | warning |
| [DAS3002](docs/rules/DAS3002.md) | EF Core | Do not re-enable tracking on a read-only query | warning |
| [DAS3003](docs/rules/DAS3003.md) | EF Core | Pass CancellationToken to EF Core async queries | warning |
| [DAS3004](docs/rules/DAS3004.md) | EF Core | Pass CancellationToken to SaveChangesAsync | warning |
| [DAS3005](docs/rules/DAS3005.md) | EF Core | Avoid database queries inside loops | warning |
| [DAS3006](docs/rules/DAS3006.md) | EF Core | Keep filtering and projection on the server | warning |
| [DAS3007](docs/rules/DAS3007.md) | EF Core | Parameterize raw SQL | error |
| [DAS3008](docs/rules/DAS3008.md) | EF Core | Bound request-driven query materialization | suggestion |
| [DAS3009](docs/rules/DAS3009.md) | EF Core | Use AnyAsync for existence checks | warning |
| [DAS3010](docs/rules/DAS3010.md) | EF Core | Do not run parallel operations on one DbContext | error |
| [DAS4001](docs/rules/DAS4001.md) | Testing | Asynchronous tests must return Task | error |

See [the full catalog](docs/rules/index.md), [best-practice playbook](docs/best-practices.md),
and [known limitations](docs/limitations.md).

## Documentation

- [Adoption guide](docs/adoption.md)
- [Configuration and suppression](docs/configuration.md)
- [Architecture](docs/architecture.md)
- [Version support](docs/version-support.md)

## Repository layout

```text
.
├── AGENTS.md                         # shared guidance for people and agents
├── config/profiles/                  # default, strict, performance, web-api
├── docs/                             # adoption, playbooks, references, rule pages
├── eng/                              # installer and integrity verification
├── rules/rules.json                  # machine-readable rule inventory
├── samples/                          # intentionally bad and production-style code
├── src/DotNetAntiSlop.Analyzers/     # netstandard2.0 Roslyn analyzer package
├── tests/DotNetAntiSlop.Analyzers.Tests/
└── .github/workflows/                # build, tests, and distribution smoke tests
```

## Build and verify

```bash
./eng/verify_repo.sh
dotnet restore dotnet-anti-slop.slnx
dotnet build dotnet-anti-slop.slnx -c Release --no-restore
dotnet test dotnet-anti-slop.slnx -c Release --no-build
```

`eng/verify_repo.sh` checks rule ID uniqueness, analyzer/profile/document
coverage, project XML, JSON, workflow structure, installer behavior, skill
provenance, line endings, and unresolved placeholders.

## Version policy

| Layer | Version |
|---|---|
| Analyzer target | `netstandard2.0` |
| Roslyn API floor | 4.8 |
| Test target | `net8.0` |
| Repository build and CI | .NET 10 |

See [version support](docs/version-support.md) for the distinction between the
analyzer host, application target framework, and Roslyn API floor.

## Important limits

Static analysis cannot prove workload size, database indexes, transaction
semantics, authorization, idempotency, or whether a particular allocation is
material. Medium-confidence diagnostics are configurable and document their
boundaries. Treat suppression as a local design decision: include a reason and
prefer the narrowest possible scope.

## Research basis

The policy is grounded primarily in official platform documentation:

- [ASP.NET Core best practices](https://learn.microsoft.com/aspnet/core/fundamentals/best-practices)
- [EF Core efficient querying](https://learn.microsoft.com/ef/core/performance/efficient-querying)
- [EF Core tracking vs. no-tracking](https://learn.microsoft.com/ef/core/querying/tracking)
- [.NET code analysis overview](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview)
- [.NET 11 what's new](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-11/overview)

A larger, rule-by-rule source map is in [docs/sources.md](docs/sources.md).

## Project

Contributions are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).
Changes are published under the [MIT License](LICENSE).
