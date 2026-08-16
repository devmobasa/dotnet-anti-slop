# C# and .NET engineering contract

Apply these rules to every generated or modified C# file, whether written by a
person or a tool, unless a narrower repository instruction overrides them.

## Correctness first

- Keep the build warning-free under the active analyzer profile.
- Do not suppress diagnostics without a short, local reason.
- Prefer the smallest behavior-preserving change. Preserve public contracts
  unless the task explicitly authorizes a breaking change.
- Propagate `CancellationToken` through asynchronous I/O and database calls.
- Never use `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` to bridge async.
- Do not write `async void` except a true event-handler boundary.
- Ensure async anonymous callbacks target a task-returning delegate, except for
  genuine event handlers.
- Await request work. Background work must be queued to a hosted service or
  durable broker and must own its dependency-injection scope.
- Do not leave catch blocks empty. Handle or propagate failures; when swallowing
  a specific expected exception is intentional, explain why at the catch site.

## C# and runtime

- Avoid repeated string accumulation in loops. Use `StringBuilder`, one
  `string.Join`, `string.Create`, or a measured span-based implementation.
- Keep value types on generic paths; do not box them through `object` or
  non-generic collections in hot loops.
- Materialize lazy sequences once when they would otherwise be enumerated more
  than once. Do not materialize earlier than needed.
- Use `Any`/`AnyAsync` for existence. Use collection `Count` properties when an
  exact count is already O(1).
- Seed collection capacity when the source count is already known.
- Use `ValueTask` only when the API and measurements justify it. Consume each
  returned `ValueTask` once.
- Create `TaskCompletionSource<T>` with
  `TaskCreationOptions.RunContinuationsAsynchronously`.
- Bound concurrency explicitly. Do not create one task per untrusted or
  unbounded input item.

## ASP.NET Core

- Use the host-owned service provider; never call `BuildServiceProvider` during
  registration.
- Respect DI lifetimes. `DbContext` and request state are scoped, never singleton.
- Use typed/named `HttpClient` or an injected client. Do not construct one per
  request.
- Accept request cancellation in asynchronous endpoints and controller actions.
- Store `IHttpContextAccessor`, not its `HttpContext`; read the current context
  only when it is needed.
- Validate strongly typed bound options during startup with `ValidateOnStart`
  or `AddOptionsWithValidateOnStart`.
- Await, return, or deliberately compose the task from
  `EventCallback.InvokeAsync`; do not discard callback completion or failures.
- Guard development diagnostics and interactive API UI.
- Bound request body, upload, response, and query sizes. Stream large payloads.
- Use structured logging templates; do not pre-format expensive messages.

## EF Core

- Keep one `DbContext` per unit of work and never operate on one context in
  parallel.
- Add `AsNoTracking()` (or configure no-tracking) for entity queries that are
  read-only. DTO/scalar projections do not need a ceremonial call.
- Apply `Where`, projection, ordering, and paging before materialization.
- Never issue a database query per loop iteration. Batch, join, or prefetch.
- Pass cancellation to EF asynchronous operators and `SaveChangesAsync`.
- Parameterize SQL. Interpolated EF APIs parameterize values; Raw APIs require
  constant SQL plus explicit parameters.
- Bound endpoint materialization with paging, a cap, streaming, or aggregation.
- Choose split queries, compiled queries, context pooling, and retry strategies
  only from measured query shape and workload evidence.

## Tests

- Async tests return `Task` or `ValueTask`, never `void`.
- Test cancellation, bounded result sizes, DI scope boundaries, and database
  query shape for critical paths.
- Do not treat mocked `DbSet` LINQ as proof that a query translates on the real
  provider. Use provider-level integration tests for important queries.
