#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
root=$(CDPATH= cd -- "$script_dir/.." && pwd -P)
project_url=https://github.com/devmobasa/dotnet-anti-slop

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

(( $# == 1 )) || fail 'usage: verify_package.sh PACKAGE'
package=$1
[[ $package == /* ]] || package=$PWD/$package
[[ -f $package ]] || fail "package does not exist: $package"

temporary_root=$(mktemp -d "${TMPDIR:-/tmp}/dotnet-anti-slop-package.XXXXXX")
cleanup() {
  rm -rf -- "$temporary_root"
}
trap cleanup EXIT HUP INT TERM

run_capture() {
  local expected_success=$1 working_directory=$2 output_file=$3
  shift 3
  local status=0
  (cd "$working_directory" && "$@") >"$output_file" 2>&1 || status=$?
  if [[ $expected_success == true && $status -ne 0 ]] || [[ $expected_success == false && $status -eq 0 ]]; then
    command cat -- "$output_file" >&2
    fail "unexpected exit code $status: $*"
  fi
}

assert_warning_free() {
  local output_file=$1 label=$2
  if grep -Fq InvalidGlobalSectionName "$output_file" || grep -Eiq '\bwarning[[:space:]]+[A-Z]+[0-9]+\b' "$output_file"; then
    command cat -- "$output_file" >&2
    fail "$label emitted a warning"
  fi
}

entries=$temporary_root/entries
zipinfo -1 "$package" | LC_ALL=C sort >"$entries"
required_entries=(
  LICENSE
  README.md
  analyzers/dotnet/cs/DotNetAntiSlop.Analyzers.dll
  buildTransitive/DotNetAntiSlop.Analyzers.props
  buildTransitive/config/default.globalconfig
  buildTransitive/config/performance.globalconfig
  buildTransitive/config/strict.globalconfig
  buildTransitive/config/web-api.globalconfig
)
for entry in "${required_entries[@]}"; do
  grep -Fxq "$entry" "$entries" || fail "package is missing entry: $entry"
done
! grep -Eq '^(lib|ref)/' "$entries" || fail 'analyzer-only package contains runtime assets'
nuspec_name=$(grep -E '\.nuspec$' "$entries" | head -n 1)
[[ -n $nuspec_name ]] || fail 'package does not contain a nuspec'
unzip -p "$package" "$nuspec_name" >"$temporary_root/package.nuspec"
nuspec=$temporary_root/package.nuspec
! grep -Fq '<dependencies' "$nuspec" || fail 'analyzer-only package contains dependency groups'
grep -Fq '<id>DotNetAntiSlop.Analyzers</id>' "$nuspec" || fail 'package metadata id is invalid'
grep -Fq '<title>DotNet Anti-Slop Analyzers</title>' "$nuspec" || fail 'package metadata title is invalid'
grep -Fq '<developmentDependency>true</developmentDependency>' "$nuspec" || fail 'package metadata developmentDependency is invalid'
grep -Eq '<license[^>]*type="expression"[^>]*>MIT</license>' "$nuspec" || fail 'package license metadata is invalid'
grep -Fq '<readme>README.md</readme>' "$nuspec" || fail 'package readme metadata is invalid'
grep -Fq "<projectUrl>$project_url</projectUrl>" "$nuspec" || fail 'package project URL is invalid'
grep -Eq "<repository[^>]*type=\"git\"[^>]*url=\"$project_url\"[^>]*commit=\"[0-9a-f]{40}\"" "$nuspec" || \
  fail 'package repository metadata is incomplete'

package_name=$(basename -- "$package")
prefix=DotNetAntiSlop.Analyzers.
[[ $package_name == "$prefix"*.nupkg ]] || fail "unexpected package name: $package_name"
version=${package_name#"$prefix"}
version=${version%.nupkg}
consumer=$temporary_root/consumer
mkdir -p -- "$consumer"
cp -- "$root/global.json" "$consumer/global.json"
expected_sdk=$(cd "$root" && dotnet --version)
actual_sdk=$(cd "$consumer" && dotnet --version)
[[ $actual_sdk == "$expected_sdk" ]] || fail "consumer selected SDK $actual_sdk; repository selected $expected_sdk"

cat >"$consumer/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <RestorePackagesPath>\$(MSBuildThisFileDirectory).packages</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DotNetAntiSlop.Analyzers" Version="$version" />
  </ItemGroup>
</Project>
EOF
cat >"$consumer/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-package" value="$(dirname -- "$package")" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
run_capture true "$consumer" "$temporary_root/restore.log" dotnet restore Consumer.csproj --configfile NuGet.config

cat >"$consumer/Consumer.cs" <<'EOF'
using System.Threading;
using System.Threading.Tasks;

public static class Consumer
{
    public static async Task RunAsync(CancellationToken cancellationToken) =>
        await Task.Delay(1, cancellationToken);
}
EOF
run_capture true "$consumer" "$temporary_root/valid.log" dotnet build Consumer.csproj --no-restore
assert_warning_free "$temporary_root/valid.log" 'valid package consumer'

cat >"$consumer/Consumer.cs" <<'EOF'
using System.Collections.Generic;

public static class Consumer
{
    public static List<int> Copy(IReadOnlyCollection<int> values)
    {
        var result = new List<int>();
        foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
    }
}
EOF
run_capture true "$consumer" "$temporary_root/default.log" dotnet build Consumer.csproj --no-restore --no-incremental -p:DotNetAntiSlopProfile=default
run_capture true "$consumer" "$temporary_root/strict.log" dotnet build Consumer.csproj --no-restore --no-incremental -p:DotNetAntiSlopProfile=strict
! grep -Fq DAS1009 "$temporary_root/default.log" || fail 'default package profile unexpectedly reports DAS1009'
grep -Eiq '\bwarning[[:space:]]+DAS1009\b' "$temporary_root/strict.log" || fail 'strict package profile did not report DAS1009 as a warning'

cat >"$consumer/Consumer.cs" <<'EOF'
using System.Threading.Tasks;

public static class Consumer
{
    public static int Run() => Task.FromResult(42).Result;
}
EOF
run_capture false "$consumer" "$temporary_root/invalid.log" dotnet build Consumer.csproj --no-restore --no-incremental -warnaserror:DAS1001
grep -Fq DAS1001 "$temporary_root/invalid.log" || fail 'invalid package consumer did not report DAS1001'

printf 'package verification passed: %s\n' "$package"
