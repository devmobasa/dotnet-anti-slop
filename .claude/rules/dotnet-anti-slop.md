# .NET anti-slop

Use `AGENTS.md` as the authoritative implementation contract. Consult
`docs/rules/index.md` before suppressing a `DASxxxx` finding. Keep cancellation,
DI scope, query translation, materialization bounds, and asynchronous ownership
visible in the code rather than implied in comments.
