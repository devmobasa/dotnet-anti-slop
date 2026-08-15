# Profiles

| Profile | Intended use |
|---|---|
| `default` | Balanced baseline for libraries, services, workers, and web applications. |
| `strict` | New codebases and CI gates; every active DAS rule is at least a warning. |
| `performance` | Hot-path review with web-host-specific policy disabled. |
| `web-api` | ASP.NET Core APIs; request, DI, cancellation, and query bounds are promoted. |

Set a NuGet consumer profile with:

```xml
<PropertyGroup>
  <DotNetAntiSlopProfile>web-api</DotNetAntiSlopProfile>
</PropertyGroup>
```

For vendored installation, rerun `eng/install.sh --profile web-api /path/to/repo`
or change the imported profile path in `.dotnet-anti-slop/DotNetAntiSlop.props`.
A project-level `.editorconfig` can lower or raise any individual diagnostic.
