# Version support

Four different versions matter:

| Layer | Policy |
|---|---|
| Analyzer target | `netstandard2.0` |
| Roslyn API floor | Microsoft.CodeAnalysis 4.8 |
| Test target | `net8.0` |
| Repository build and CI | .NET SDK 10 |
| Repository development SDK | .NET 10.0.400 or a compatible stable feature band |

The repository uses `.slnx`, which requires SDK 9.0.200 or newer, and pins a
stable .NET 10 feature band for development and CI. The test project targets
`net8.0`; the .NET 10 SDK supplies the required test runtime in CI. The
repository does not claim separate runtime-target validation for applications.

## Why the analyzer does not target `net11.0`

Roslyn loads analyzers inside the compiler host, not inside the target
application. A low analyzer target and conservative compiler API surface allow
one analyzer binary to inspect projects targeting newer frameworks.

## Language versions

The analyzer processes the syntax tree supplied by the host compiler. It does
not force the consumer's `LangVersion`. This repository itself uses `latest`
while avoiding runtime dependencies that would break the `netstandard2.0`
analyzer target.

## Support floor changes

Raising the Roslyn API floor is a compatibility decision. Before doing so:

1. identify the semantic capability that cannot be implemented on 4.8;
2. test loading under the oldest and newest supported compiler hosts;
3. document which IDE/build hosts stop working;
4. release a new major package version if consumers are affected.
