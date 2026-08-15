# EF Core review checklist

- Is the `DbContext` short-lived, scoped/factory-created, and never used in
  parallel?
- Is a read-only entity query no-tracking? Is a DTO/scalar projection used when
  entities are unnecessary?
- Are filters, projection, ordering, and pagination before materialization?
- What guarantees the maximum row/column/result size?
- Is there any terminal database operation in a loop or lazy-loading path?
- Does every async query and save receive cancellation?
- Is `AnyAsync` used for existence and `CountAsync` only for exact counts?
- Is raw SQL constant/parameterized and are dynamic identifiers allow-listed?
- Are include/split-query choices based on graph shape and consistency needs?
- Are updates set-based or reasonably batched?
- Are concurrency tokens, retries, idempotency, and transaction boundaries
  explicit?
- Has generated SQL and the real database plan been checked for critical paths?
- Do integration tests exercise the actual provider behavior and query count?
