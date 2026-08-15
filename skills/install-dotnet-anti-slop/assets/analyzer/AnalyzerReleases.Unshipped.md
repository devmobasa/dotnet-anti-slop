### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DAS1001 | Async | Warning | Avoid sync-over-async
DAS1002 | Async | Warning | Do not call Thread.Sleep from asynchronous code
DAS1003 | Async | Error | Avoid async void outside event handlers
DAS1004 | Async | Warning | Forward the available CancellationToken
DAS1005 | Async | Warning | Do not replace an available token with CancellationToken.None
DAS1006 | Performance | Warning | Avoid string accumulation in loops
DAS1007 | LINQ | Warning | Use Any for an existence check
DAS1008 | LINQ | Warning | Avoid repeated enumeration of a lazy sequence
DAS1009 | Collections | Info | Set collection capacity when the size is knowable
DAS1010 | Performance | Warning | Avoid boxing in hot loops
DAS1011 | Async | Error | Consume a ValueTask only once
DAS1012 | Concurrency | Warning | Bound asynchronous fan-out
DAS1013 | Async | Warning | Run TaskCompletionSource continuations asynchronously
DAS1014 | Async | Error | Do not convert async lambdas to void-returning delegates
DAS2001 | ASP.NET Core | Error | Do not build a nested service provider
DAS2002 | Dependency Injection | Error | Do not register DbContext as a singleton
DAS2003 | Dependency Injection | Error | Do not capture a scoped dependency in a singleton
DAS2004 | ASP.NET Core | Warning | Request handlers should accept CancellationToken
DAS2005 | HTTP | Warning | Do not construct HttpClient per request
DAS2006 | ASP.NET Core | Warning | Guard development-only middleware
DAS2007 | ASP.NET Core | Error | Do not fire-and-forget request work
DAS2008 | ASP.NET Core | Warning | Do not cache IHttpContextAccessor.HttpContext
DAS3001 | EF Core | Warning | Use no-tracking for read-only entity queries
DAS3002 | EF Core | Warning | Do not re-enable tracking on a read-only query
DAS3003 | EF Core | Warning | Pass CancellationToken to EF Core async queries
DAS3004 | EF Core | Warning | Pass CancellationToken to SaveChangesAsync
DAS3005 | EF Core | Warning | Avoid database queries inside loops
DAS3006 | EF Core | Warning | Keep filtering and projection on the server
DAS3007 | EF Core | Error | Parameterize raw SQL
DAS3008 | EF Core | Info | Bound request-driven query materialization
DAS3009 | EF Core | Warning | Use AnyAsync for existence checks
DAS3010 | EF Core | Error | Do not run parallel operations on one DbContext
DAS4001 | Testing | Error | Asynchronous tests must return Task
