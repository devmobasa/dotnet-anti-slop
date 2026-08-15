# C# review checklist

- Are allocations in loops intentional and measured?
- Does string construction use one expression, `Join`, or a sized builder?
- Does any value type cross an `object`/interface/non-generic boundary in a hot
  path?
- Is a lazy sequence enumerated exactly as many times as intended?
- Is collection capacity known, bounded, and supplied where useful?
- Are lambdas static or non-capturing in high-frequency paths when practical?
- Does every async API return a task-like result and propagate cancellation?
- Is concurrency explicitly bounded by downstream capacity?
- Are `ValueTask`, pooling, spans, and unsafe code backed by measurement?
- Are nullable contracts, ownership, and disposal visible in the API?
