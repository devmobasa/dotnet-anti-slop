# Known limitations

The project favors useful, explainable signal over claims of perfect program
analysis.

## General

- Analysis is per compilation. Registrations, wrappers, and generated members
  in another assembly may not be visible.
- Reflection, `dynamic`, source-generated call sites, and custom query
  providers can hide semantics.
- Diagnostic severity cannot know workload volume. Medium-confidence rules
  should be calibrated from telemetry.
- The analyzer does not inspect database schemas, indexes, query plans, HTTP
  gateway limits, or deployment topology.

## Runtime rules

- Repeated enumeration is identifier-local and does not model aliasing,
  memoizing iterators, or helper methods.
- Capacity inference recognizes obvious collection-sized loops, not arbitrary
  arithmetic.
- Boxing is reported only at loop arguments with an actual compiler boxing
  conversion.
- Fan-out detection recognizes inline `WhenAll(...Select(...))`; tasks staged
  through fields/collections need review.
- Cancellation overload matching is conservative and can miss extension or
  generic overload relationships.
- Task-completion-source analysis reports omitted or compile-time-constant
  creation options. It does not guess the flags in a runtime options value or
  trace construction through a factory.
- Async delegate analysis uses the resolved delegate return type and exempts a
  direct event subscription. It does not infer event semantics hidden behind a
  custom registration API.

## ASP.NET Core rules

- Endpoint detection covers resolved controller and common minimal API mapping
  symbols. Custom endpoint frameworks need their own analyzers/configuration.
- Singleton/scoped analysis sees constructor injection and same-compilation
  registrations. Factory internals and runtime conditional registrations are
  outside its model.
- Development middleware may be intentionally exposed under a custom security
  gate the analyzer cannot understand. Suppress the exact call with a reason.
- Fire-and-forget analysis detects bare/discarded tasks; a poorly designed
  custom queue returning `Task` may need an explicit wrapper.
- HttpContext capture analysis covers direct property reads assigned to fields
  or properties. It deliberately allows local snapshots and does not model
  values passed through helpers or stored indirectly.

## EF Core rules

- Read-only intent is inferred from GET/HEAD handlers and conventional method
  prefixes. Command/query architectures with different names can configure or
  suppress `DAS3001`.
- DTO/scalar projection detection compares `DbSet<TEntity>` with the query
  element type. Complex nested entity projections may need review.
- Client-side shaping detection is strongest for one expression chain; a list
  stored in a local and filtered much later is not always connected.
- Raw SQL allows compile-time constant text. It does not validate explicit
  parameter use, identifier allow-lists, permissions, or SQL correctness.
- N+1 detection is syntactic loop containment. Lazy-loading N+1 during
  serialization requires command interception/telemetry.
- Parallel-context detection covers inline `Task.WhenAll` arguments sharing a
  resolved context symbol.
- Query bounds recognize visible `Take` and single-row terminals. Domain-level
  constraints are not inferred.

## Rule authoring principle

Do not “fix” a limitation by adding a broad text match. Add semantic evidence,
a positive test, and a realistic false-positive test. If that cannot produce
stable signal, document the practice in the review playbook instead.
