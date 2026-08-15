$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent $PSScriptRoot

bash (Join-Path $PSScriptRoot "verify_repo.sh")
if ($LASTEXITCODE -ne 0) { throw "Repository verification failed with exit code $LASTEXITCODE" }

dotnet restore (Join-Path $root "dotnet-anti-slop.slnx")
if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE" }

dotnet build (Join-Path $root "dotnet-anti-slop.slnx") -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

dotnet test (Join-Path $root "dotnet-anti-slop.slnx") -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE" }
