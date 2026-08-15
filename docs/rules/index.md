# Diagnostic catalog

All `DASxxxx` diagnostics are semantic Roslyn analyzers. The default severity is
a policy starting point, not a substitute for codebase-specific risk decisions.

| ID | Category | Rule | Default | Signal |
|---|---|---|---|---|
| [DAS1001](DAS1001.md) | Async | Avoid sync-over-async | warning | high |
| [DAS1002](DAS1002.md) | Async | Do not call Thread.Sleep from asynchronous code | warning | high |
| [DAS1003](DAS1003.md) | Async | Avoid async void outside event handlers | error | high |
| [DAS1004](DAS1004.md) | Async | Forward the available CancellationToken | warning | high |
| [DAS1005](DAS1005.md) | Async | Do not replace an available token with CancellationToken.None | warning | high |
| [DAS1006](DAS1006.md) | Performance | Avoid string accumulation in loops | warning | high |
| [DAS1007](DAS1007.md) | LINQ | Use Any for an existence check | warning | high |
| [DAS1008](DAS1008.md) | LINQ | Avoid repeated enumeration of a lazy sequence | warning | medium |
| [DAS1009](DAS1009.md) | Collections | Set collection capacity when the size is knowable | suggestion | medium |
| [DAS1010](DAS1010.md) | Performance | Avoid boxing in hot loops | warning | high |
| [DAS1011](DAS1011.md) | Async | Consume a ValueTask only once | error | high |
| [DAS1012](DAS1012.md) | Concurrency | Bound asynchronous fan-out | warning | medium |
| [DAS1013](DAS1013.md) | Async | Run TaskCompletionSource continuations asynchronously | warning | high |
| [DAS1014](DAS1014.md) | Async | Do not convert async lambdas to void-returning delegates | error | high |
| [DAS2001](DAS2001.md) | ASP.NET Core | Do not build a nested service provider | error | high |
| [DAS2002](DAS2002.md) | Dependency Injection | Do not register DbContext as a singleton | error | high |
| [DAS2003](DAS2003.md) | Dependency Injection | Do not capture a scoped dependency in a singleton | error | medium |
| [DAS2004](DAS2004.md) | ASP.NET Core | Request handlers should accept CancellationToken | warning | high |
| [DAS2005](DAS2005.md) | HTTP | Do not construct HttpClient per request | warning | high |
| [DAS2006](DAS2006.md) | ASP.NET Core | Guard development-only middleware | warning | medium |
| [DAS2007](DAS2007.md) | ASP.NET Core | Do not fire-and-forget request work | error | high |
| [DAS2008](DAS2008.md) | ASP.NET Core | Do not cache IHttpContextAccessor.HttpContext | warning | high |
| [DAS3001](DAS3001.md) | EF Core | Use no-tracking for read-only entity queries | warning | medium |
| [DAS3002](DAS3002.md) | EF Core | Do not re-enable tracking on a read-only query | warning | high |
| [DAS3003](DAS3003.md) | EF Core | Pass CancellationToken to EF Core async queries | warning | high |
| [DAS3004](DAS3004.md) | EF Core | Pass CancellationToken to SaveChangesAsync | warning | high |
| [DAS3005](DAS3005.md) | EF Core | Avoid database queries inside loops | warning | high |
| [DAS3006](DAS3006.md) | EF Core | Keep filtering and projection on the server | warning | high |
| [DAS3007](DAS3007.md) | EF Core | Parameterize raw SQL | error | high |
| [DAS3008](DAS3008.md) | EF Core | Bound request-driven query materialization | suggestion | medium |
| [DAS3009](DAS3009.md) | EF Core | Use AnyAsync for existence checks | warning | high |
| [DAS3010](DAS3010.md) | EF Core | Do not run parallel operations on one DbContext | error | high |
| [DAS4001](DAS4001.md) | Testing | Asynchronous tests must return Task | error | high |

## Severity model

- **Error**: unsafe ownership, lifetime, SQL composition, task semantics, or
  context concurrency that should block new code.
- **Warning**: a strong correctness or scalability smell with legitimate but
  uncommon exceptions.
- **Suggestion**: a workload-sensitive optimization worth reviewing in hot or
  request-facing paths.

Change severity in a project `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.DAS3008.severity = warning
```

Suppress a single justified instance with a pragma or `SuppressMessage` and put
the design reason beside it. Do not globally disable a rule to accommodate one
exception.
