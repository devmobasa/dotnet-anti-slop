# C# and .NET anti-slop playbook

This playbook contains both automated rules and review guidance. A practice is
automated only when syntax and symbols provide a useful signal. Anything that
depends on workload shape, provider behavior, query plans, security boundaries,
or benchmark data remains a review item.

## 1. Make performance work evidence-driven

Correctness, bounded resource use, and ownership are non-negotiable. Micro-
optimizations are not. Before replacing ordinary code with pooling, spans,
custom builders, unsafe code, compiled queries, or `ValueTask`, capture a
baseline with production-like data and a representative call path.

Use:

- BenchmarkDotNet for isolated CPU/allocation comparisons;
- `dotnet-counters` for allocation rate, GC, thread-pool, exception, and runtime
  counters;
- `dotnet-trace` or a profiler for call stacks and contention;
- OpenTelemetry/Application Insights/another tracing backend for end-to-end
  request and dependency latency;
- database-native query plans and server metrics for EF Core work.

Optimize the largest measured cost while preserving an easy rollback. A clever
optimization without a benchmark, load shape, and regression test is often a
maintenance liability.

## 2. Allocations, memory, and GC

### Strings

Strings are immutable. Repeatedly appending to a growing string in a loop
copies prior content and creates short-lived objects. Prefer, in order:

1. a single interpolation or concatenation expression when the number of
   segments is fixed;
2. `string.Join` for a sequence that already exists;
3. `StringBuilder` with a reasonable initial capacity for variable repeated
   appends;
4. `string.Create`, `Span<char>`, or a purpose-built buffer only when profiling
   shows the builder itself is material.

Do not mechanically replace every interpolation. The compiler can lower fixed
concatenations efficiently, and interpolation is usually clearer. Also avoid
recommending `System.Text.ValueStringBuilder` as though it were a normal public
BCL type; the runtime uses an internal implementation. A project may implement
or adopt a ref-struct builder, but doing so requires ownership, stack-size, and
pool-return discipline.

For logging, use message templates rather than building a string before the
log-level check:

```csharp
logger.LogInformation(
    "Processed {OrderCount} orders for customer {CustomerId}",
    orderCount,
    customerId);
```

For very hot log sites, source-generated logging or `LoggerMessage` avoids
repeated template parsing and some boxing.

### Boxing

Boxing occurs when a value type is converted to `object` or an interface
without a constrained/generic path. Common sources include:

- non-generic collections such as `ArrayList` and `Hashtable`;
- `params object[]` APIs;
- interface calls on unconstrained structs;
- formatting/logging overloads that accept `object`;
- storing heterogeneous value types in object-based caches.

Keep hot paths generic and strongly typed. Do not redesign a clear API merely
to remove an occasional, measured-negligible box.

### Closures and delegates

A lambda that captures locals normally needs a closure object. In a frequently
executed path:

- use a `static` lambda and pass state explicitly;
- hoist immutable delegates when the target is stable;
- avoid creating predicates and callbacks in inner loops;
- be careful with LINQ convenience in allocation-sensitive code.

Do not ban lambdas. A captured lambda allocated once at startup is irrelevant
to request throughput; a new capture per item can be material.

### Collections

Choose the collection for its access pattern. Then:

- initialize `List<T>`, `Dictionary<TKey,TValue>`, `HashSet<T>`, `Queue<T>`, and
  `Stack<T>` with capacity when the final size is already known or tightly
  bounded;
- avoid `ToList()` merely to call `Count`, `Any`, or one terminal operation;
- materialize a lazy sequence once when it would otherwise execute twice;
- use `TryGetValue` rather than a lookup followed by an indexer;
- use a comparer intentionally for string keys;
- consider `FrozenDictionary`/`FrozenSet` for immutable, read-heavy lookup
  tables built once;
- consider `ArrayPool<T>` only when buffers are large/frequent enough to matter
  and every path returns the buffer, ideally clearing sensitive references.

A capacity equal to hostile input size is not automatically safe. Validate or
cap the input first.

### Large objects and retention

Large arrays and strings place pressure on the large-object heap. More often,
the real problem is not one allocation but retention:

- caches without size/expiration limits;
- tracking too many EF entities;
- buffering whole request/response bodies;
- long-lived delegates that retain object graphs;
- static events and timers not unsubscribed/disposed;
- returning pooled buffers before consumers finish;
- keeping `Memory<T>` backed by a large owner for a tiny slice.

Prefer streaming, pagination, bounded caches, and explicit ownership. Dispose
`IDisposable`/`IAsyncDisposable` objects at the layer that owns them.

### Structs, spans, and stack allocation

Use structs for small value semantics, not as a universal allocation escape.
Large mutable structs are expensive to copy and easy to misuse. Mark immutable
value types `readonly` where appropriate and pass large structs by `in` only
after measurement.

`Span<T>` is excellent for synchronous, stack-confined slicing and parsing, but
cannot cross ordinary async/yield boundaries. `Memory<T>` is the heap-safe
counterpart. Keep `stackalloc` sizes small and bounded; never derive an
unlimited stack allocation directly from request input.

## 3. Async, cancellation, and concurrency

### Async all the way

Do not bridge asynchronous APIs with `.Result`, `.Wait()`, or
`GetAwaiter().GetResult()`. Blocking:

- consumes a worker while the operation is incomplete;
- increases thread-pool growth and context switching;
- can deadlock in environments with a synchronization context;
- hides cancellation and exception flow.

Make the caller async and continue upward. At a truly synchronous external
boundary, isolate the bridge and document why the contract cannot change.

Use `Task.Delay`, a timer, a channel, a semaphore, or another asynchronous
primitive instead of `Thread.Sleep` in async or server code.

### Cancellation

A cancellation token is part of the operation contract, not decoration.

- accept it on asynchronous methods that perform I/O, wait, retry, or
  potentially long CPU work;
- put it last unless an established API shape requires otherwise;
- forward it to HTTP, database, stream, channel, semaphore, delay, and child
  operations;
- check it periodically in CPU loops;
- do not convert an available token to `CancellationToken.None`;
- distinguish caller cancellation from timeout when mapping errors and metrics.

In ASP.NET Core, a `CancellationToken` endpoint/action parameter is bound to
`HttpContext.RequestAborted`. In hosted services, combine shutdown and
operation-specific tokens carefully and dispose linked token sources.

Cancellation is cooperative. After a side effect reaches a point that must
commit atomically, a short non-cancellable completion phase can be legitimate.
Make that phase explicit so callers do not infer stronger cancellation
guarantees than the system can provide.

### `Task` versus `ValueTask`

Return `Task` by default. Consider `ValueTask` only when all of the following
are true:

- the API is invoked frequently;
- synchronous completion is common;
- the avoided `Task` allocation is visible in measurement;
- callers can obey single-consumption semantics;
- the extra API and state-machine complexity is justified.

A `ValueTask` should normally be awaited once. If it must be consumed more than
once, call `AsTask()` once and retain the resulting task. Do not put
`ValueTask<T>` into broad APIs casually; it has more misuse modes than `Task<T>`.

### `TaskCompletionSource<T>`

Create `TaskCompletionSource<T>` with
`TaskCreationOptions.RunContinuationsAsynchronously`. Without that option, code
that completes the source can run consumer continuations inline, coupling the
producer to arbitrary continuation work and making deadlocks or thread
starvation easier to trigger.

### `async void`

Use `async void` only for a genuine event-handler signature. Library,
application, controller, endpoint, command, and test methods return `Task` or
`ValueTask`. A task makes completion, exceptions, cancellation, and testing
composable.

The same rule applies to anonymous callbacks: an `async` lambda or anonymous
method should target a task-returning delegate such as `Func<Task>`. A
void-returning delegate such as `Action` cannot expose completion or exceptions
to its caller. A direct subscription to a genuine event remains the intentional
exception.

### Bound fan-out

`Task.WhenAll(items.Select(ProcessAsync))` starts one operation per item as the
projection is enumerated. This is safe only when the input is small and trusted.
For variable or hostile sizes:

- use `Parallel.ForEachAsync` with `MaxDegreeOfParallelism`;
- use a `SemaphoreSlim` around each operation;
- use a bounded `Channel<T>` and fixed consumer count;
- batch work at the database or remote API;
- respect downstream connection-pool and rate-limit capacity.

Preserve result ordering only when required; it can increase buffering.

### Shared mutable state

Do not assume async code is single-threaded. Avoid sharing mutable state between
requests. For state that must be shared:

- prefer immutable snapshots;
- use a concurrency-specific collection or lock;
- keep lock scopes short and never hold a monitor across `await`;
- use `SemaphoreSlim` for asynchronous mutual exclusion;
- define ordering for multiple locks;
- include cancellation and timeout behavior.

`DbContext` is a unit-of-work object and does not support parallel operations.
Two concurrent database calls require separate contexts/scopes or sequential
awaits.

### Async streams

`IAsyncEnumerable<T>` can reduce buffering, but it does not make an unbounded
operation safe by itself. Use `[EnumeratorCancellation]` in iterator methods,
propagate cancellation with `WithCancellation`, keep database/context lifetime
valid for the enumeration, and understand whether the transport/provider
actually streams or buffers.

### `ConfigureAwait`

Do not add a blanket analyzer that demands `ConfigureAwait(false)` in ASP.NET
Core application code. ASP.NET Core does not install the classic ASP.NET
request synchronization context, and indiscriminate calls add noise. A reusable
library may choose a consistent policy based on its supported environments.

## 4. LINQ and data transformation

LINQ is an abstraction over different execution engines. Ask what owns the
query:

- `IEnumerable<T>` executes in process;
- `IQueryable<T>` builds an expression for a provider;
- `IAsyncEnumerable<T>` pulls values asynchronously;
- a materialized collection has already paid the query/allocation cost.

Avoid:

- `Count() > 0` when only existence matters;
- `Any()` followed by `ToList()` on the same lazy source;
- `Where`/`Select` after `ToList` when the provider could have done the work;
- hidden quadratic operations such as `list.Contains` inside a large loop;
- calling arbitrary methods inside an EF expression without checking
  translation.

Prefer direct loops in extremely hot code when profiling shows LINQ delegate,
iterator, or closure overhead. Otherwise, favor clear query composition.

## 5. ASP.NET Core

### Keep the request path asynchronous and bounded

Request handlers should:

- accept request cancellation;
- avoid synchronous network, file, and database I/O;
- avoid `Thread.Sleep` and blocking task waits;
- cap collection results, uploads, decompression, and request-body buffering;
- stream large payloads when the serializer and client can consume them;
- reject invalid size/range/page parameters before expensive work;
- avoid loading a complete body merely to inspect a small prefix.

Synchronous I/O can starve the worker pool under load. A fast local benchmark
with one request does not expose that failure mode.

### Dependency injection lifetimes

The built-in container manages disposal and scopes. Do not call
`BuildServiceProvider()` while registering services. It creates a second graph
with its own singletons and disposables.

Typical lifetimes:

- singleton: thread-safe, process-wide, no request/scoped capture;
- scoped: one instance per request or explicitly created scope;
- transient: new resolution each time; still disposed by its owner/container
  when registered and resolved through DI.

`DbContext` is scoped by default. A singleton needing scoped work should
depend on an explicit abstraction such as a queue, `IServiceScopeFactory`, or
`IDbContextFactory<TContext>` and create/dispose a scope per operation. Never
retain `HttpContext`, a controller, or request-specific state in a singleton.

When code needs ambient request state, store `IHttpContextAccessor` and read its
current `HttpContext` at the point of use. Do not cache the accessor's
`HttpContext` in a field or property: it may be absent during construction or
belong to a different request later.

Validate scopes in development and tests.

### `HttpClient`

Use an injected/typed client or `IHttpClientFactory` in ASP.NET Core. Configure:

- a logical base address and default headers;
- per-request timeout/cancellation semantics;
- resilience policies appropriate to idempotency;
- DNS/handler lifetimes;
- connection limits where required;
- redaction and tracing.

Do not retry non-idempotent operations blindly. A timeout implemented by
cancellation must be distinguishable from request cancellation in telemetry.

### Background work

Do not start a task and abandon it from an endpoint. Request services may be
disposed immediately after the response, exceptions become unobserved, and
shutdown can discard work.

For short work that must complete before response: await it.

For deferred work:

1. validate and persist/enqueue the command durably;
2. return an operation identifier or accepted response;
3. process it in a `BackgroundService`, queue consumer, or external worker;
4. create a fresh DI scope per item;
5. handle retries, poison messages, idempotency, and shutdown explicitly.

`Task.Run` is not a durable background system.

### Middleware and environment guards

Development exception pages, migration endpoints, and interactive API
documentation can disclose internals. Keep them inside an environment guard or
an explicit authenticated production policy.

Middleware order is behavior. Review exception handling, forwarded headers,
HTTPS, routing, CORS, authentication, authorization, rate limiting, output
caching, and endpoint mapping as one pipeline. Trust forwarded headers only
from configured proxies/networks.

### Caching and rate limits

Cache only data with a clear key, freshness policy, invalidation strategy, and
tenant/security boundary. Prevent cache stampedes on expensive misses. Set
size limits for in-memory caches.

Use rate limiting and concurrency limiting at a boundary that can identify the
caller and operation cost. A request-count limit alone may not protect a
database-heavy endpoint.

### Serialization and responses

Project database results into response DTOs. Do not serialize tracked entity
graphs directly:

- navigation cycles and lazy loading can trigger unexpected queries;
- over-posting/over-sharing becomes easier;
- persistence shape becomes an API contract;
- tracking state is retained longer.

Use source-generated JSON metadata or AOT-compatible options when startup,
trimming, or native AOT is a requirement, and test the exact deployment mode.

### Observability

Use structured logs, distributed traces, metrics, and health checks with
bounded cardinality. Never put user IDs, raw URLs, SQL text, exception messages,
or arbitrary input into metric dimensions without a cardinality/privacy plan.

Log once at the layer that can add action. Avoid catching an exception merely
to log and rethrow when higher middleware already logs it.

## 6. EF Core

### `DbContext` lifetime and ownership

Treat a context as a short unit of work:

- create it through DI, a factory, or a pool;
- await every operation before starting another on the same instance;
- dispose it at the scope boundary;
- do not cache it or store it in a singleton;
- do not use it after an unrecoverable EF operation exception;
- clear the tracker or use a new context for large batch loops.

Context pooling can reduce setup cost but makes context instances reusable.
Mutable per-request state must be reset correctly. Pool only after measuring
context-construction overhead and reviewing tenant/state injection.

### Query only what is needed

Compose on `IQueryable<T>` and materialize last:

```csharp
var page = await db.Orders
    .AsNoTracking()
    .Where(order => order.CustomerId == customerId)
    .OrderBy(order => order.Id)
    .Where(order => afterId == null || order.Id > afterId)
    .Take(pageSize)
    .Select(order => new OrderSummary(
        order.Id,
        order.Number,
        order.Total))
    .ToListAsync(cancellationToken);
```

This controls rows, columns, tracking, ordering, and cancellation in one place.

A projection to non-entity DTO/scalar values does not need a ceremonial
`AsNoTracking`; EF does not track scalar values, though entity instances nested
inside a projection can still be tracked. `AsNoTrackingWithIdentityResolution`
can deduplicate repeated entity instances without attaching them to the
context, at an additional temporary cost.

Use tracking when the context will update the entities or when identity/fix-up
semantics are part of the operation. Do not detach everything after paying to
track it.

### Bound result sets

Every request-facing collection query needs a bound: `Take`, pagination,
aggregation, streaming with a server-side predicate, or a domain-guaranteed
single-row terminal.

Offset pagination is simple but can grow expensive and can shift under
concurrent writes. Keyset/seek pagination is often better for sequential
navigation:

```csharp
query = query
    .Where(order => order.Id > cursor)
    .OrderBy(order => order.Id)
    .Take(pageSize);
```

Use a deterministic unique ordering. Enforce maximum page size server-side.

### Avoid N+1

A database terminal inside a loop is a strong N+1 smell. Prefer:

- one `WHERE key IN (...)` query, with batching for parameter limits;
- a join or grouped projection;
- explicit eager loading for the exact shape;
- loading related data into a dictionary once;
- a set-based update/delete.

Lazy loading makes N+1 easy to hide during serialization or mapping. Use it only
with strict query visibility and tests.

### Projection, includes, and split queries

Projection is usually preferable to `Include` for API reads. It transfers only
required columns and makes the response shape explicit.

For entity graphs, choose single versus split queries based on shape:

- a single query can duplicate parent columns and create cartesian explosion
  across multiple collections;
- split queries issue more round trips but avoid large join multiplication;
- ordering and consistency across split commands need review.

Do not create a universal analyzer demanding `AsSplitQuery`; query shape,
provider, network latency, and consistency requirements decide it.

### Existence and counts

Use `AnyAsync(predicate, token)` for existence and `CountAsync` only when the
number is needed. Do not run `AnyAsync` and then issue the real query when one
bounded query can answer the operation.

### Raw SQL

Prefer LINQ. When SQL is necessary:

- use interpolated EF APIs for value parameterization;
- or use a constant Raw SQL template with explicit parameters;
- never concatenate identifiers or values from request input;
- identifiers such as table/column names cannot be ordinary parameters—map
  them from a strict allow-list;
- keep authorization and row/tenant filtering explicit;
- review command timeout and result bounds.

Parameterization addresses injection of values; it does not make arbitrary SQL
structure or elevated database permissions safe.

### Saving and concurrency

Pass cancellation to `SaveChangesAsync`, but understand the transaction
boundary. Use optimistic concurrency tokens where lost updates matter and map
`DbUpdateConcurrencyException` to a domain-specific retry/conflict policy.

For large set-based changes, `ExecuteUpdate[Async]` and
`ExecuteDelete[Async]` can avoid loading entities. They bypass the change
tracker, so reconcile any tracked state and audit/domain-event assumptions.

Avoid calling `SaveChanges` once per item. Stage a reasonable batch, save, and
clear/use a new context. Very large imports may need provider-native bulk
facilities.

### Transactions, execution strategies, and retries

A retry strategy can replay operations. Retried work must be idempotent or use a
deduplication key. Coordinate explicit transactions with the provider execution
strategy as documented; do not wrap arbitrary external side effects in a
database retry loop.

Keep transactions short. Do not hold a database transaction while calling a
remote API unless the architecture explicitly accepts the lock duration and
failure coupling.

### Compiled queries

EF caches query compilation by expression shape. Explicit compiled queries can
help very hot, stable queries, but are not a universal improvement. Benchmark
the exact provider and query. Avoid dynamic expression shapes that defeat cache
reuse before reaching for compiled queries.

### Indexes and plans

No C# analyzer can prove the right index. For important paths, inspect generated
SQL and the database plan with realistic cardinality. Verify filters, joins,
sorts, covering columns, parameter sensitivity, and lock behavior. Keep query
tags useful but low-cardinality and free of secrets.

### Testing EF Core

Test important queries against the actual provider family or a close disposable
instance. Mocked `DbSet`/LINQ and the in-memory provider can differ in:

- translation support;
- null, collation, and case semantics;
- transactions and constraints;
- generated values and concurrency;
- raw SQL and provider functions.

Unit-test domain logic separately, then use integration tests for translation,
schema, and behavior. Assert query counts or intercept commands for N+1-
sensitive workflows.

## 7. Testing and build discipline

- Treat analyzer warnings as CI failures for new code, with a baseline strategy
  for legacy adoption.
- Async tests return `Task`; pass cancellation/timeouts to prevent hung suites.
- Use deterministic clocks, IDs, and random sources through abstractions.
- Avoid shared mutable fixtures unless access is serialized intentionally.
- Test maximum sizes, cancellation, retry/idempotency, and concurrent updates,
  not only the happy path.
- Run analyzers in generated-code-aware mode and do not edit generated output.
- Build with the oldest supported SDK and a current/preview SDK lane to catch
  host compatibility issues.

## 8. Analyzer compatibility policy

The analyzer:

- targets the analyzer to `netstandard2.0` and Roslyn 4.8 APIs;
- avoids preview-only syntax/API dependencies in the analyzer implementation;
- parses consumer code with the host compiler, so newer C# syntax remains
  analyzable;
- builds and runs its test suite with the repository's pinned .NET 10 SDK;
- keeps version-specific recommendations in documentation until APIs stabilize.

Do not copy preview API names into broad rules until their release contract is
stable and the rule has false-positive tests.

## 9. Review checklist

Before merging a service or data-path change, answer:

1. What bounds the input, output, retries, buffering, fan-out, cache, and query?
2. Where does cancellation enter, and is it forwarded to every wait/I/O call?
3. Who owns every task, stream, context, scope, timer, and disposable?
4. Which work executes in process, over HTTP, and in the database?
5. Does EF shape/filter/project before materialization and avoid loop queries?
6. Are DI lifetimes compatible, especially singleton-to-scoped dependencies?
7. Is dynamic SQL, logging, serialization, and metrics data-safe?
8. What measurement or failure-mode test justifies complex optimization?
9. Does the change pass repository validation and both distribution smoke tests?
