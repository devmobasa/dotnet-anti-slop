# Testing review checklist

- Do async tests return `Task`/`ValueTask` and have a timeout/cancellation plan?
- Are clocks, IDs, randomness, and external dependencies deterministic?
- Are fixtures isolated or explicitly serialized?
- Do API tests cover maximum sizes, cancellation, auth, and rate limits?
- Do EF tests validate translation and behavior on the real provider family?
- Is N+1-sensitive behavior checked with command interception/query counts?
- Are retry and idempotency paths tested with partial failure?
- Does CI run the oldest supported and newest/preview analyzer hosts?
- Are analyzer suppressions reviewed as code, with a reason and narrow scope?
