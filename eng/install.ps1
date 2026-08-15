[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $TargetPath,

    [ValidateSet("default", "strict", "performance", "web-api")]
    [string] $Profile = "default",

    [string] $VendorDir = ".dotnet-anti-slop",

    [switch] $Force,
    [switch] $Uninstall,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$BeginMarker = "<!-- dotnet-anti-slop:begin -->"
$EndMarker = "<!-- dotnet-anti-slop:end -->"
$SourceRepository = "https://github.com/devmobasa/dotnet-anti-slop"
$IgnoredDirectories = @("bin", "obj", ".vs", "artifacts")

function Resolve-ExistingDirectory([string] $Path) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Container)) {
        throw "Target directory does not exist: $Path"
    }

    return $resolved.Path
}

function Assert-VendorDirectory([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or
        [IO.Path]::IsPathRooted($Path) -or
        ($Path -split '[\\/]' | Where-Object { $_ -eq ".." })) {
        throw "-VendorDir must be a non-empty repository-relative path"
    }
}

function Get-MappedFiles(
    [string] $Root,
    [string] $Prefix
) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return @()
    }

    $rootPath = (Resolve-Path -LiteralPath $Root).Path
    return @(
        Get-ChildItem -LiteralPath $rootPath -File -Recurse |
            Where-Object {
                $relativeParts = $_.FullName.Substring($rootPath.Length).TrimStart('\', '/') -split '[\\/]'
                -not ($relativeParts | Where-Object { $_ -in $IgnoredDirectories })
            } |
            ForEach-Object {
                $relative = $_.FullName.Substring($rootPath.Length).TrimStart('\', '/').Replace('\', '/')
                [pscustomobject]@{
                    Relative = "$Prefix/$relative"
                    Path = $_.FullName
                }
            }
    )
}

function Get-ContentDigest([object[]] $Entries) {
    $sha = [Security.Cryptography.IncrementalHash]::CreateHash(
        [Security.Cryptography.HashAlgorithmName]::SHA256
    )
    try {
        foreach ($entry in $Entries | Sort-Object Relative) {
            $nameBytes = [Text.Encoding]::UTF8.GetBytes([string] $entry.Relative)
            $sha.AppendData($nameBytes)
            $zero = [byte[]]@(0)
            $sha.AppendData($zero)
            $content = [IO.File]::ReadAllBytes([string] $entry.Path)
            $sha.AppendData($content)
            $sha.AppendData($zero)
        }

        return -join ($sha.GetHashAndReset() | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $sha.Dispose()
    }
}

function Get-SkillPayloadDigest([string] $Root) {
    $entries = @()
    foreach ($directory in @("scripts", "assets")) {
        $entries += Get-MappedFiles (Join-Path $Root $directory) $directory
    }

    $entries = @($entries | Where-Object { $_.Relative -ne "assets/provenance.json" })
    return Get-ContentDigest $entries
}

function Get-CanonicalPayloadDigest([string] $Root) {
    $entries = @(
        [pscustomobject]@{ Relative = "scripts/install.sh"; Path = (Join-Path $Root "eng/install.sh") }
        [pscustomobject]@{ Relative = "scripts/install.ps1"; Path = (Join-Path $Root "eng/install.ps1") }
    )
    $entries += Get-MappedFiles (Join-Path $Root "src/DotNetAntiSlop.Analyzers") "assets/analyzer"
    $entries += Get-MappedFiles (Join-Path $Root "config/profiles") "assets/config/profiles"
    $entries += Get-MappedFiles (Join-Path $Root "templates") "assets/templates"
    $entries += [pscustomobject]@{ Relative = "assets/agent-guidance.md"; Path = (Join-Path $Root "AGENTS.md") }
    return Get-ContentDigest $entries
}

function Get-CanonicalGitState([string] $Root) {
    $gitRoot = (& git -C $Root rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $gitRoot) {
        return [pscustomobject]@{ Revision = "unversioned-source"; State = "unversioned" }
    }

    if ((Resolve-Path -LiteralPath $gitRoot).Path -ne (Resolve-Path -LiteralPath $Root).Path) {
        return [pscustomobject]@{ Revision = "unversioned-source"; State = "unversioned" }
    }

    $revision = (& git -C $Root rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $revision) {
        return [pscustomobject]@{ Revision = "unversioned-source"; State = "unversioned" }
    }

    $status = (& git -C $Root status --porcelain -- AGENTS.md config eng/install.sh eng/install.ps1 src/DotNetAntiSlop.Analyzers templates 2>$null)
    $state = if ($LASTEXITCODE -ne 0) { "unknown" } elseif ($status) { "dirty" } else { "clean" }
    return [pscustomobject]@{ Revision = $revision.Trim(); State = $state }
}

function Get-SourceProvenance(
    [string] $Root,
    [string] $SkillAssets
) {
    $provenancePath = Join-Path $SkillAssets "provenance.json"
    if (Test-Path -LiteralPath $provenancePath -PathType Leaf) {
        $embedded = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
        $keys = @($embedded.PSObject.Properties.Name | Sort-Object)
        $expectedKeys = @("content_sha256", "schema_version", "source_repository", "source_revision")
        if (Compare-Object $keys $expectedKeys) {
            throw "Skill provenance has an invalid schema"
        }
        if ($embedded.schema_version -ne 1) {
            throw "Skill provenance has an unsupported schema version"
        }
        if ($embedded.source_repository -ne $SourceRepository) {
            throw "Skill provenance contains an unexpected source repository"
        }
        $revision = [string] $embedded.source_revision
        if ($revision -notmatch '^(?:[0-9a-f]{40}|unversioned-source)$' -or
            $revision -eq ("0" * 40)) {
            throw "Skill provenance does not contain a source revision"
        }
        $expectedDigest = [string] $embedded.content_sha256
        if ($expectedDigest -notmatch '^[0-9a-f]{64}$') {
            throw "Skill provenance does not contain a content digest"
        }

        $actualDigest = Get-SkillPayloadDigest $Root
        $state = if ($actualDigest -eq $expectedDigest) { "synchronized-snapshot" } else { "modified-skill" }
        return [pscustomobject]@{
            Repository = $embedded.source_repository
            Revision = $embedded.source_revision
            State = $state
            ContentSha256 = $actualDigest
        }
    }

    $git = Get-CanonicalGitState $Root
    return [pscustomobject]@{
        Repository = $SourceRepository
        Revision = $git.Revision
        State = $git.State
        ContentSha256 = Get-CanonicalPayloadDigest $Root
    }
}

function Copy-SourceTree(
    [string] $Source,
    [string] $Destination
) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -Force |
        Copy-Item -Destination $Destination -Recurse -Force
    Get-ChildItem -LiteralPath $Destination -Directory -Recurse |
        Where-Object { $_.Name -in $IgnoredDirectories } |
        Sort-Object FullName -Descending |
        Remove-Item -Recurse -Force
}

function Remove-ManagedBlock([string] $Content) {
    $start = $Content.IndexOf($BeginMarker, [StringComparison]::Ordinal)
    if ($start -lt 0) {
        return $Content
    }

    $finish = $Content.IndexOf($EndMarker, $start, [StringComparison]::Ordinal)
    if ($finish -lt 0) {
        throw "Found '$BeginMarker' without matching '$EndMarker'"
    }

    $finish += $EndMarker.Length
    while ($finish -lt $Content.Length -and $Content[$finish] -in "`r", "`n") {
        $finish++
    }

    return $Content.Substring(0, $start).TrimEnd() + "`n" + $Content.Substring($finish).TrimStart()
}

function Update-MSBuildImport(
    [string] $Path,
    [string] $ImportedFile,
    [bool] $Remove
) {
    if ($Remove -and -not (Test-Path -LiteralPath $Path)) {
        return
    }

    $content = if (Test-Path -LiteralPath $Path) {
        Get-Content -LiteralPath $Path -Raw
    }
    else {
        "<Project>`n</Project>`n"
    }

    if ($Remove -and -not $content.Contains($BeginMarker)) {
        return
    }

    $content = Remove-ManagedBlock $content
    if (-not $Remove) {
        $closing = $content.LastIndexOf("</Project>", [StringComparison]::Ordinal)
        if ($closing -lt 0) {
            throw "$Path is not an MSBuild project: missing </Project>"
        }

        $msbuildPath = $VendorDir.Replace('\', '/')
        $project = '$(MSBuildThisFileDirectory)' + $msbuildPath + "/$ImportedFile"
        $block = @"
$BeginMarker
  <Import Project="$project" Condition="Exists('$project')" />
$EndMarker
"@
        $indented = ($block.TrimEnd() -split "`r?`n" | ForEach-Object { "  $_" }) -join "`n"
        $content = $content.Substring(0, $closing).TrimEnd() + "`n$indented`n" + $content.Substring($closing).TrimStart()
    }

    if (-not $content.EndsWith("`n")) {
        $content += "`n"
    }

    if ($DryRun) {
        Write-Output "would update: $Path"
    }
    else {
        [IO.File]::WriteAllText($Path, $content, [Text.UTF8Encoding]::new($false))
        Write-Output "updated: $Path"
    }
}

function Quote-PowerShell([string] $Value) {
    return "'" + $Value.Replace("'", "''") + "'"
}

try {
    Assert-VendorDirectory $VendorDir
    $targetRoot = Resolve-ExistingDirectory $TargetPath
    $vendorRoot = Join-Path $targetRoot $VendorDir
    $directoryBuildProps = Join-Path $targetRoot "Directory.Build.props"
    $directoryBuildTargets = Join-Path $targetRoot "Directory.Build.targets"

    $sourceRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
    $skillAssets = Join-Path $sourceRoot "assets"
    if (Test-Path -LiteralPath (Join-Path $skillAssets "analyzer") -PathType Container) {
        $analyzerSource = Join-Path $skillAssets "analyzer"
        $profileBase = Join-Path $skillAssets "config/profiles"
    }
    else {
        $analyzerSource = Join-Path $sourceRoot "src/DotNetAntiSlop.Analyzers"
        $profileBase = Join-Path $sourceRoot "config/profiles"
    }

    if ($Uninstall) {
        Update-MSBuildImport $directoryBuildProps "DotNetAntiSlop.props" $true
        Update-MSBuildImport $directoryBuildTargets "DotNetAntiSlop.targets" $true
        if (Test-Path -LiteralPath $vendorRoot) {
            if ($DryRun) {
                Write-Output "would remove: $vendorRoot"
            }
            else {
                Remove-Item -LiteralPath $vendorRoot -Recurse -Force
                Write-Output "removed: $vendorRoot"
            }
        }
        exit 0
    }

    if ((Test-Path -LiteralPath $vendorRoot) -and -not $Force) {
        throw "$vendorRoot already exists; use -Force to refresh it"
    }

    $profileSource = Join-Path $profileBase "$Profile.globalconfig"
    if (-not (Test-Path -LiteralPath $analyzerSource -PathType Container) -or
        -not (Test-Path -LiteralPath $profileSource -PathType Leaf)) {
        throw "Installer source tree is incomplete"
    }

    $provenance = Get-SourceProvenance $sourceRoot $skillAssets
    if ($DryRun) {
        Write-Output "would copy: $analyzerSource -> $(Join-Path $vendorRoot 'analyzer')"
        Write-Output "would copy: $profileSource -> $(Join-Path $vendorRoot "config/$Profile.globalconfig")"
    }
    else {
        if (Test-Path -LiteralPath $vendorRoot) {
            Remove-Item -LiteralPath $vendorRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Path (Join-Path $vendorRoot "config") -Force | Out-Null
        Copy-SourceTree $analyzerSource (Join-Path $vendorRoot "analyzer")
        Copy-Item -LiteralPath $profileSource -Destination (Join-Path $vendorRoot "config/$Profile.globalconfig")

        $props = @"
<Project>
  <ItemGroup Condition="'`$(IsDotNetAntiSlopAnalyzerProject)' != 'true'">
    <GlobalAnalyzerConfigFiles
      Include="`$(MSBuildThisFileDirectory)config/$Profile.globalconfig" />
  </ItemGroup>
</Project>
"@
        $targets = @'
<Project>
  <ItemGroup Condition="'$(IsDotNetAntiSlopAnalyzerProject)' != 'true'">
    <ProjectReference
      Include="$(MSBuildThisFileDirectory)analyzer/DotNetAntiSlop.Analyzers.csproj"
      OutputItemType="Analyzer"
      ReferenceOutputAssembly="false"
      PrivateAssets="all" />
  </ItemGroup>
</Project>
'@
        [IO.File]::WriteAllText((Join-Path $vendorRoot "DotNetAntiSlop.props"), $props.TrimStart() + "`n", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $vendorRoot "DotNetAntiSlop.targets"), $targets.TrimStart() + "`n", [Text.UTF8Encoding]::new($false))

        $refresh = "& $(Quote-PowerShell $PSCommandPath) $(Quote-PowerShell $targetRoot) -Profile $Profile -Force"
        $installation = @"
# Vendored dotnet-anti-slop

Profile: ``$Profile``

Source: ``$($provenance.Repository)``

Source revision: ``$($provenance.Revision)``

Source state: ``$($provenance.State)``

Source content SHA-256: ``$($provenance.ContentSha256)``

Refresh from the same policy checkout or installed skill with:

``$refresh``

Compare the vendored directory with the source before forcing a refresh; local analyzer changes are owned by this repository.
"@
        [IO.File]::WriteAllText((Join-Path $vendorRoot "INSTALLATION.md"), $installation.TrimStart() + "`n", [Text.UTF8Encoding]::new($false))
        Write-Output "installed: $vendorRoot"
    }

    Update-MSBuildImport $directoryBuildProps "DotNetAntiSlop.props" $false
    Update-MSBuildImport $directoryBuildTargets "DotNetAntiSlop.targets" $false
}
catch {
    Write-Error "error: $($_.Exception.Message)"
    exit 2
}
