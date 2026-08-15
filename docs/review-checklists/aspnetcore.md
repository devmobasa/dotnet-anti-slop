# ASP.NET Core review checklist

- Does every async endpoint/action accept request cancellation?
- Is all request-path I/O asynchronous?
- What caps request body, decompression, upload, page, response, and fan-out?
- Are DI lifetimes valid? Does any singleton retain scoped/request state?
- Is `HttpClient` injected/configured and is retry behavior idempotent?
- Is deferred work queued durably and processed in its own scope?
- Are development diagnostics and interactive API UI appropriately guarded?
- Is middleware ordered correctly for proxy headers, errors, routing, CORS,
  authentication, authorization, rate limiting, and caching?
- Are logs structured, low-cardinality, and free of secrets?
- Are serialization DTOs explicit rather than tracked entity graphs?
- Do cache keys include tenant/security context and have size/freshness limits?
- Are timeouts, cancellations, retries, and dependency failures observable?
