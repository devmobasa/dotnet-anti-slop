# Samples

The files in this directory are review fixtures rather than projects in the
main solution.

- `bad/` intentionally demonstrates findings from every diagnostic family.
- `good/` demonstrates production-style alternatives.
- `web-api/` shows a bounded, cancellable ASP.NET Core + EF Core endpoint slice.

The analyzer unit tests are the executable semantic specification. Samples are
kept out of the solution so intentionally bad code does not break CI.
