using Microsoft.CodeAnalysis;

namespace DotNetAntiSlop.Analyzers;

internal static class RuleDescriptors
{
    private const string DocumentationBaseUrl =
        "https://github.com/devmobasa/dotnet-anti-slop/blob/main/docs/rules/";

    public static readonly DiagnosticDescriptor DAS1001 = Create(
        DiagnosticIds.DAS1001,
        "Avoid sync-over-async",
        "Await asynchronous work instead of blocking with Result, Wait, or GetAwaiter().GetResult().",
        "Async",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS1002 = Create(
        DiagnosticIds.DAS1002,
        "Do not call Thread.Sleep from asynchronous code",
        "Use an awaited delay or another non-blocking coordination primitive.",
        "Async",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS1003 = Create(
        DiagnosticIds.DAS1003,
        "Avoid async void outside event handlers",
        "Return Task or ValueTask so callers can await completion and observe exceptions.",
        "Async",
        DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DAS1004 = Create(
        DiagnosticIds.DAS1004,
        "Forward the available CancellationToken",
        "Pass the current operation's token to cancellable asynchronous calls.",
        "Async",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS1005 = Create(
        DiagnosticIds.DAS1005,
        "Do not replace an available token with CancellationToken.None",
        "Use the token already available in the current operation.",
        "Async",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS1006 = Create(
        DiagnosticIds.DAS1006,
        "Avoid string accumulation in loops",
        "Use StringBuilder, a buffer, string.Create, or a single join operation for repeated concatenation.",
        "Performance",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS1007 = Create(
        DiagnosticIds.DAS1007,
        "Use Any for an existence check",
        "Do not enumerate a LINQ sequence with Count or LongCount only to compare the result with zero.",
        "LINQ",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS1008 = Create(
        DiagnosticIds.DAS1008,
        "Avoid repeated enumeration of a lazy sequence",
        "Materialize once or restructure the operation when the same lazy sequence is enumerated repeatedly.",
        "LINQ",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS1009 = Create(
        DiagnosticIds.DAS1009,
        "Set collection capacity when the size is knowable",
        "Provide an initial capacity when a collection is populated from a source with a known count.",
        "Collections",
        DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor DAS1010 = Create(
        DiagnosticIds.DAS1010,
        "Avoid boxing in hot loops",
        "Keep value types on generic or strongly typed paths inside loops.",
        "Performance",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS1011 = Create(
        DiagnosticIds.DAS1011,
        "Consume a ValueTask only once",
        "Await a ValueTask once, or convert it to a Task once when repeated consumption is required.",
        "Async",
        DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DAS1012 = Create(
        DiagnosticIds.DAS1012,
        "Bound asynchronous fan-out",
        "Do not feed an unbounded projection directly into Task.WhenAll.",
        "Concurrency",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS1013 = Create(
        DiagnosticIds.DAS1013,
        "Run TaskCompletionSource continuations asynchronously",
        "Create TaskCompletionSource with TaskCreationOptions.RunContinuationsAsynchronously.",
        "Async",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS1014 = Create(
        DiagnosticIds.DAS1014,
        "Do not convert async lambdas to void-returning delegates",
        "Use a Task-returning delegate so completion and exceptions remain observable.",
        "Async",
        DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DAS2001 = Create(
        DiagnosticIds.DAS2001,
        "Do not build a nested service provider",
        "Let the host build and own the application service provider.",
        "ASP.NET Core",
        DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DAS2002 = Create(
        DiagnosticIds.DAS2002,
        "Do not register DbContext as a singleton",
        "Use AddDbContext or a scoped registration for DbContext.",
        "Dependency Injection",
        DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DAS2003 = Create(
        DiagnosticIds.DAS2003,
        "Do not capture a scoped dependency in a singleton",
        "Redesign the lifetime boundary or create an explicit scope at operation time.",
        "Dependency Injection",
        DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DAS2004 = Create(
        DiagnosticIds.DAS2004,
        "Request handlers should accept CancellationToken",
        "Accept the request-aborted token in asynchronous controllers, minimal API handlers, and middleware delegates.",
        "ASP.NET Core",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS2005 = Create(
        DiagnosticIds.DAS2005,
        "Do not construct HttpClient per request",
        "Inject HttpClient, use a typed client, or use IHttpClientFactory.",
        "HTTP",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS2006 = Create(
        DiagnosticIds.DAS2006,
        "Guard development-only middleware",
        "Place developer exception pages, migration endpoints, and interactive API UI behind an environment or explicit security guard.",
        "ASP.NET Core",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS2007 = Create(
        DiagnosticIds.DAS2007,
        "Do not fire-and-forget request work",
        "Await request work or enqueue a durable/background operation that owns its scope.",
        "ASP.NET Core",
        DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DAS2008 = Create(
        DiagnosticIds.DAS2008,
        "Do not cache IHttpContextAccessor.HttpContext",
        "Store IHttpContextAccessor and read its current HttpContext when needed.",
        "ASP.NET Core",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS3001 = Create(
        DiagnosticIds.DAS3001,
        "Use no-tracking for read-only entity queries",
        "Add AsNoTracking or configure no-tracking behavior when entities are materialized only for reading.",
        "EF Core",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS3002 = Create(
        DiagnosticIds.DAS3002,
        "Do not re-enable tracking on a read-only query",
        "Remove AsTracking from read-only query chains, especially after AsNoTracking.",
        "EF Core",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS3003 = Create(
        DiagnosticIds.DAS3003,
        "Pass CancellationToken to EF Core async queries",
        "Supply the current token to EF Core asynchronous terminal operators.",
        "EF Core",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS3004 = Create(
        DiagnosticIds.DAS3004,
        "Pass CancellationToken to SaveChangesAsync",
        "Supply the current token when persisting changes.",
        "EF Core",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS3005 = Create(
        DiagnosticIds.DAS3005,
        "Avoid database queries inside loops",
        "Batch keys, join, project, or prefetch instead of issuing one query per iteration.",
        "EF Core",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS3006 = Create(
        DiagnosticIds.DAS3006,
        "Keep filtering and projection on the server",
        "Apply Where, Select, ordering, and paging before materialization.",
        "EF Core",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS3007 = Create(
        DiagnosticIds.DAS3007,
        "Parameterize raw SQL",
        "Use interpolated EF APIs or explicit parameters rather than composing user or variable data into Raw SQL APIs.",
        "EF Core",
        DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DAS3008 = Create(
        DiagnosticIds.DAS3008,
        "Bound request-driven query materialization",
        "Page, cap, stream, or aggregate entity collections returned from request handlers.",
        "EF Core",
        DiagnosticSeverity.Info);
    public static readonly DiagnosticDescriptor DAS3009 = Create(
        DiagnosticIds.DAS3009,
        "Use AnyAsync for existence checks",
        "Use AnyAsync rather than comparing CountAsync with zero.",
        "EF Core",
        DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor DAS3010 = Create(
        DiagnosticIds.DAS3010,
        "Do not run parallel operations on one DbContext",
        "Await each operation before starting the next, or give concurrent operations separate scopes and DbContext instances.",
        "EF Core",
        DiagnosticSeverity.Error);
    public static readonly DiagnosticDescriptor DAS4001 = Create(
        DiagnosticIds.DAS4001,
        "Asynchronous tests must return Task",
        "Use Task or ValueTask for asynchronous test methods; never async void.",
        "Testing",
        DiagnosticSeverity.Error);

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string message,
        string category,
        DiagnosticSeverity severity)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            message,
            category,
            severity,
            isEnabledByDefault: true,
            description: message,
            helpLinkUri: DocumentationBaseUrl + id + ".md");
    }
}
