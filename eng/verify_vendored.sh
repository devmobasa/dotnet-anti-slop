#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
root=$(CDPATH= cd -- "$script_dir/.." && pwd -P)
skill=$root/skills/install-dotnet-anti-slop

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

temporary_root=$(mktemp -d "${TMPDIR:-/tmp}/dotnet-anti-slop-vendored.XXXXXX")
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

initialize_git_repository() {
  local repository=$1
  git -C "$repository" init --quiet
  git -C "$repository" config user.name 'dotnet-anti-slop test'
  git -C "$repository" config user.email test@invalid.local
  git -C "$repository" add .
  git -C "$repository" commit --quiet -m fixture
  git -C "$repository" rev-parse HEAD
}

create_consumer() {
  local consumer=$1
  mkdir -p -- "$consumer"
  cp -- "$root/global.json" "$consumer/global.json"
  local expected_sdk actual_sdk
  expected_sdk=$(cd "$root" && dotnet --version)
  actual_sdk=$(cd "$consumer" && dotnet --version)
  [[ $actual_sdk == "$expected_sdk" ]] || fail "consumer selected SDK $actual_sdk; repository selected $expected_sdk"
  cat >"$consumer/Directory.Packages.props" <<'EOF'
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
EOF
  cat >"$consumer/Directory.Build.props" <<'EOF'
<Project>
  <PropertyGroup>
    <ExistingConsumerProperty>preserved</ExistingConsumerProperty>
  </PropertyGroup>
</Project>
EOF
  cat >"$consumer/Directory.Build.targets" <<'EOF'
<Project>
  <Target Name="ExistingTarget" BeforeTargets="BeforeBuild" />
</Project>
EOF
  cat >"$consumer/Consumer.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
EOF
}

verify_canonical_dirty_state() {
  local base=$1 source="$1/canonical source" target="$1/canonical target" revision installation
  mkdir -p -- "$source/eng" "$source/src" "$source/config"
  cp -- "$root/AGENTS.md" "$source/AGENTS.md"
  cp -- "$root/eng/install.sh" "$source/eng/install.sh"
  cp -- "$root/eng/install.ps1" "$source/eng/install.ps1"
  cp -R -- "$root/src/DotNetAntiSlop.Analyzers" "$source/src/DotNetAntiSlop.Analyzers"
  find "$source/src/DotNetAntiSlop.Analyzers" -type d \( -name bin -o -name obj \) -prune -exec rm -rf -- {} +
  cp -R -- "$root/config/profiles" "$source/config/profiles"
  cp -R -- "$root/templates" "$source/templates"
  revision=$(initialize_git_repository "$source")
  printf '\n' >>"$source/AGENTS.md"
  mkdir -p -- "$target"
  "$source/eng/install.sh" "$target" >/dev/null
  installation=$target/.dotnet-anti-slop/INSTALLATION.md
  grep -Fq "Source revision: \`$revision\`" "$installation" || fail 'canonical installer did not record its strict Git root'
  grep -Fq 'Source state: `dirty`' "$installation" || fail 'canonical installer did not mark modified source content dirty'
}

unrelated="$temporary_root/unrelated checkout"
mkdir -p -- "$unrelated"
printf 'unrelated\n' >"$unrelated/marker.txt"
unrelated_revision=$(initialize_git_repository "$unrelated")
standalone_skill="$unrelated/nested/install skill"
mkdir -p -- "$(dirname -- "$standalone_skill")"
cp -R -- "$skill" "$standalone_skill"
installer=$standalone_skill/scripts/install.sh
provenance=$standalone_skill/assets/provenance.json
source_revision=$(jq -r .source_revision "$provenance")
content_sha256=$(jq -r .content_sha256 "$provenance")
source_repository=$(jq -r .source_repository "$provenance")

tampered_skill="$unrelated/nested/tampered install skill"
cp -R -- "$standalone_skill" "$tampered_skill"
tampered_provenance=$tampered_skill/assets/provenance.json
sed -i "s#$source_repository#https://invalid.example/repository#" "$tampered_provenance"
tampered_target="$temporary_root/tampered target"
mkdir -p -- "$tampered_target"
run_capture false "$unrelated" "$temporary_root/tampered-repository.log" "$tampered_skill/scripts/install.sh" "$tampered_target"
grep -Fq 'unexpected source repository' "$temporary_root/tampered-repository.log" || fail 'installer did not reject tampered provenance metadata'
cp -- "$provenance" "$tampered_provenance"
sed -i -E 's/"source_revision": "[^"]+"/"source_revision": "0000000000000000000000000000000000000000"/' "$tampered_provenance"
run_capture false "$unrelated" "$temporary_root/tampered-revision.log" "$tampered_skill/scripts/install.sh" "$tampered_target"
grep -Fq 'source revision' "$temporary_root/tampered-revision.log" || fail 'installer did not reject an all-zero provenance revision'

consumer="$temporary_root/consumer target with spaces"
create_consumer "$consumer"
project=$consumer/Consumer.csproj
(cd "$unrelated" && "$installer" "$consumer") >/dev/null
installation=$consumer/.dotnet-anti-slop/INSTALLATION.md
! grep -Fq "$unrelated_revision" "$installation" || fail 'standalone skill inherited an unrelated parent Git revision'
grep -Fq "Source revision: \`$source_revision\`" "$installation" || fail 'standalone skill did not use embedded source revision'
grep -Fq "Source content SHA-256: \`$content_sha256\`" "$installation" || fail 'standalone skill did not use embedded content digest'
grep -Fq 'Source state: `synchronized-snapshot`' "$installation" || fail 'synchronized standalone skill was not identified correctly'
grep -Fq "'$installer'" "$installation" || fail 'refresh command did not quote installer path containing spaces'
grep -Fq "'$consumer'" "$installation" || fail 'refresh command did not quote target path containing spaces'
grep -Fq ExistingTarget "$consumer/Directory.Build.targets" || fail 'installer replaced existing Directory.Build.targets content'
grep -Fq ExistingConsumerProperty "$consumer/Directory.Build.props" || fail 'installer replaced existing Directory.Build.props content'

cat >"$consumer/Consumer.cs" <<'EOF'
using System.Threading;
using System.Threading.Tasks;

public static class Consumer
{
    public static async Task RunAsync(CancellationToken cancellationToken) =>
        await Task.Delay(1, cancellationToken);
}
EOF
run_capture true "$consumer" "$temporary_root/valid.log" dotnet build "$project"
assert_warning_free "$temporary_root/valid.log" 'valid vendored consumer'

cat >"$consumer/Consumer.cs" <<'EOF'
using System.Threading.Tasks;

public static class Consumer
{
    public static async void Run()
    {
        await Task.Delay(1);
    }
}
EOF
run_capture false "$consumer" "$temporary_root/default.log" dotnet build "$project" --no-restore --no-incremental
(cd "$unrelated" && "$installer" "$consumer" --profile performance --force) >/dev/null
printf '\n' >>"$consumer/Consumer.cs"
run_capture true "$consumer" "$temporary_root/performance.log" dotnet build "$project" --no-incremental
grep -Eiq '\berror[[:space:]]+DAS1003\b' "$temporary_root/default.log" || fail 'default vendored profile did not report DAS1003 as an error'
grep -Eiq '\bwarning[[:space:]]+DAS1003\b' "$temporary_root/performance.log" || fail 'performance vendored profile did not report DAS1003 as a warning'

printf '\nmodified\n' >>"$standalone_skill/assets/agent-guidance.md"
(cd "$unrelated" && "$installer" "$consumer" --profile performance --force) >/dev/null
grep -Fq 'Source state: `modified-skill`' "$installation" || fail 'modified standalone skill was not marked dirty'

cat >"$consumer/Consumer.cs" <<'EOF'
using System.Threading.Tasks;

public static class Consumer
{
    public static int Run() => Task.FromResult(42).Result;
}
EOF
run_capture false "$consumer" "$temporary_root/invalid.log" dotnet build "$project" --no-incremental -warnaserror:DAS1001
grep -Fq DAS1001 "$temporary_root/invalid.log" || fail 'vendored analyzer did not report DAS1001'

verify_canonical_dirty_state "$temporary_root"
printf 'vendored consumer and provenance verification passed\n'
