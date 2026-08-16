#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
root=$(CDPATH= cd -- "$script_dir/.." && pwd -P)
expected_rule_count=36
begin_marker='<!-- dotnet-anti-slop:begin -->'

fail() {
  printf 'verification failed: %s\n' "$*" >&2
  exit 1
}

for required_command in git jq rg xmllint; do
  command -v "$required_command" >/dev/null 2>&1 || fail "required command is unavailable: $required_command"
done

temporary_root=$(mktemp -d "${TMPDIR:-/tmp}/dotnet-anti-slop-verify.XXXXXX")
cleanup() {
  rm -rf -- "$temporary_root"
}
trap cleanup EXIT HUP INT TERM

compare_sets() {
  local label=$1 expected=$2 actual=$3 missing extra
  missing=$(comm -23 "$expected" "$actual")
  extra=$(comm -13 "$expected" "$actual")
  if [[ -n $missing || -n $extra ]]; then
    fail "$label mismatch; missing=${missing:-none}, extra=${extra:-none}"
  fi
}

rules_json=$root/rules/rules.json
jq -e '
  .schemaVersion == 1 and
  .repository == "dotnet-anti-slop" and
  .supported == {
    analyzerTarget: "netstandard2.0",
    roslynApiFloor: "4.8",
    testTarget: "net8.0",
    sdkAnalyzerHosts: ["8.0", "9.0", "10.0", "11.0-preview"]
  } and
  (.rules | type == "array")
' "$rules_json" >/dev/null || fail 'rules/rules.json has invalid repository or support metadata'
jq -r '.rules[].id | select(type == "string")' "$rules_json" | LC_ALL=C sort >"$temporary_root/expected-ids"
rule_count=$(wc -l <"$temporary_root/expected-ids" | tr -d ' ')
[[ $rule_count -eq $expected_rule_count ]] || fail "expected $expected_rule_count rules, found $rule_count"
duplicate_ids=$(uniq -d "$temporary_root/expected-ids")
[[ -z $duplicate_ids ]] || fail "duplicate IDs in rules.json: $duplicate_ids"

sed -n 's/.*const string \(DAS[0-9][0-9][0-9][0-9]\) = "\(DAS[0-9][0-9][0-9][0-9]\)";.*/\1 \2/p' \
  "$root/src/DotNetAntiSlop.Analyzers/DiagnosticIds.cs" >"$temporary_root/constants"
while read -r left right; do
  [[ $left == "$right" ]] || fail "mismatched DiagnosticIds constant: $left != $right"
done <"$temporary_root/constants"
awk '{print $1}' "$temporary_root/constants" | LC_ALL=C sort -u >"$temporary_root/actual-ids"
compare_sets DiagnosticIds.cs "$temporary_root/expected-ids" "$temporary_root/actual-ids"

sed -n 's/.*DiagnosticDescriptor \(DAS[0-9][0-9][0-9][0-9]\).*/\1/p' \
  "$root/src/DotNetAntiSlop.Analyzers/RuleDescriptors.cs" | LC_ALL=C sort -u >"$temporary_root/descriptors"
compare_sets RuleDescriptors.cs "$temporary_root/expected-ids" "$temporary_root/descriptors"

rg -o 'RuleDescriptors\.DAS[0-9]{4}' "$root/src/DotNetAntiSlop.Analyzers"/*Analyzer.cs |
  sed 's/.*RuleDescriptors\.//' | LC_ALL=C sort -u >"$temporary_root/supported"
compare_sets SupportedDiagnostics "$temporary_root/expected-ids" "$temporary_root/supported"

while IFS= read -r rule_id; do
  rule_doc=$root/docs/rules/$rule_id.md
  [[ -f $rule_doc ]] || fail "missing rule documentation: docs/rules/$rule_id.md"
  grep -Fq "# $rule_id:" "$rule_doc" || fail "malformed rule heading: docs/rules/$rule_id.md"
  rg -q "$rule_id" "$root/tests/DotNetAntiSlop.Analyzers.Tests"/*.cs || fail "rule ID not referenced by tests: $rule_id"
done <"$temporary_root/expected-ids"

profile_names=$(find "$root/config/profiles" -maxdepth 1 -type f -name '*.globalconfig' -printf '%f\n' | LC_ALL=C sort)
expected_profiles=$'default.globalconfig\nperformance.globalconfig\nstrict.globalconfig\nweb-api.globalconfig'
[[ $profile_names == "$expected_profiles" ]] || fail 'profile set does not match expected profile names'
while IFS= read -r profile; do
  rg -o 'DAS[0-9]{4}' "$profile" | LC_ALL=C sort -u >"$temporary_root/profile-ids"
  compare_sets "${profile#"$root/"}" "$temporary_root/expected-ids" "$temporary_root/profile-ids"
  grep -Fq 'dotnet_diagnostic.ASP0000.severity = none' "$profile" || fail "$(basename "$profile") must suppress duplicate ASP0000"
  [[ $(head -n 1 "$profile") == 'is_global = true' ]] || fail "$(basename "$profile") is not a global analyzer config"
  ! grep -Eq '^[[:space:]]*\[.*\][[:space:]]*$' "$profile" || fail "$(basename "$profile") contains an invalid named global-config section"
done < <(find "$root/config/profiles" -maxdepth 1 -type f -name '*.globalconfig' | LC_ALL=C sort)

while IFS= read -r -d '' json_file; do
  jq -e . "$json_file" >/dev/null || fail "invalid JSON: ${json_file#"$root/"}"
done < <(find "$root" -type f -name '*.json' ! -path '*/bin/*' ! -path '*/obj/*' -print0)

while IFS= read -r -d '' xml_file; do
  xmllint --noout "$xml_file" 2>/dev/null || fail "invalid XML: ${xml_file#"$root/"}"
done < <(find "$root" -type f \( -name '*.csproj' -o -name '*.props' -o -name '*.targets' -o -name '*.slnx' \) \
  ! -path '*/bin/*' ! -path '*/obj/*' -print0)

while IFS= read -r -d '' yaml_file; do
  [[ -s $yaml_file ]] || fail "YAML is empty: ${yaml_file#"$root/"}"
  ! grep -q $'\t' "$yaml_file" || fail "YAML contains a tab: ${yaml_file#"$root/"}"
done < <(find "$root" -type f \( -name '*.yml' -o -name '*.yaml' \) ! -path '*/.git/*' -print0)

while IFS= read -r -d '' text_file; do
  case $text_file in */bin/*|*/obj/*|*/.git/*) continue ;; esac
  grep -Iq . "$text_file" || continue
  if [[ $text_file != *.ps1 ]] && LC_ALL=C grep -q $'\r' "$text_file"; then
    fail "CRLF outside PowerShell file: ${text_file#"$root/"}"
  fi
  if [[ -s $text_file ]] && [[ $(tail -c 1 "$text_file" | od -An -t u1 | tr -d ' ') != 10 ]]; then
    fail "missing final newline: ${text_file#"$root/"}"
  fi
  relative=${text_file#"$root/"}
  private_repository_marker='dotnet-anti-slop-''wip'
  private_home_marker='/home/''user/'
  if grep -Fq "$private_repository_marker" "$text_file" || grep -Fq "$private_home_marker" "$text_file"; then
    fail "private release marker in $relative"
  fi
  case $relative in
    CONTRIBUTING.md|eng/verify_repo.sh) ;;
    *)
      if grep -Eiq 'TODO|TBD|CHANGEME|example\.com/repository|google\.com/goto' "$text_file"; then
        fail "unresolved placeholder in $relative"
      fi
      ;;
  esac
done < <(find "$root" -type f -print0)
[[ ! -e $root/docs/temp ]] || fail 'internal docs/temp content must not be present in the public tree'

for required_public_file in AGENTS.md CONTRIBUTING.md LICENSE README.md; do
  [[ -f $root/$required_public_file ]] || fail "missing public repository file: $required_public_file"
done

while IFS= read -r markdown_file; do
  while IFS= read -r link_target; do
    link_target=${link_target#']('}
    link_target=${link_target%')'}
    link_target=${link_target#'<'}
    link_target=${link_target%'>'}
    case $link_target in
      ''|'#'*|*://*|mailto:*) continue ;;
    esac
    link_target=${link_target%%#*}
    link_target=${link_target%%\?*}
    [[ -e $(dirname -- "$markdown_file")/$link_target ]] ||
      fail "broken local link in ${markdown_file#"$root/"}: $link_target"
  done < <(rg -o '\]\([^)]*\)' "$markdown_file" || true)
done < <(find "$root" -type f -name '*.md' ! -path '*/bin/*' ! -path '*/obj/*' ! -path '*/.git/*' | LC_ALL=C sort)

grep -Fq 'src/DotNetAntiSlop.Analyzers/DotNetAntiSlop.Analyzers.csproj' "$root/dotnet-anti-slop.slnx" || fail 'solution missing analyzer project'
grep -Fq 'tests/DotNetAntiSlop.Analyzers.Tests/DotNetAntiSlop.Analyzers.Tests.csproj' "$root/dotnet-anti-slop.slnx" || fail 'solution missing test project'

installer_target=$temporary_root/installer-target
mkdir -p -- "$installer_target"
"$root/eng/install.sh" "$installer_target" --profile web-api --vendor-dir eng/policy >/dev/null
vendor=$installer_target/eng/policy
[[ -f $vendor/analyzer/DotNetAntiSlop.Analyzers.csproj ]] || fail 'installer did not copy analyzer project'
[[ -f $vendor/config/web-api.globalconfig ]] || fail 'installer did not copy active profile'
[[ $(grep -Fc "$begin_marker" "$installer_target/Directory.Build.props") -eq 1 ]] || fail 'installer props marker missing or duplicated'
[[ $(grep -Fc "$begin_marker" "$installer_target/Directory.Build.targets") -eq 1 ]] || fail 'installer targets marker missing or duplicated'
"$root/eng/install.sh" "$installer_target" --profile web-api --vendor-dir eng/policy --force >/dev/null
[[ $(grep -Fc "$begin_marker" "$installer_target/Directory.Build.props") -eq 1 ]] || fail 'installer props import is not idempotent'
[[ $(grep -Fc "$begin_marker" "$installer_target/Directory.Build.targets") -eq 1 ]] || fail 'installer targets import is not idempotent'
"$root/eng/install.sh" "$installer_target" --vendor-dir eng/policy --uninstall >/dev/null
[[ ! -e $vendor ]] || fail 'uninstall did not remove vendored directory'
! grep -Fq "$begin_marker" "$installer_target/Directory.Build.props" || fail 'uninstall did not remove props marker'
! grep -Fq "$begin_marker" "$installer_target/Directory.Build.targets" || fail 'uninstall did not remove targets marker'
never_installed=$temporary_root/never-installed
mkdir -p -- "$never_installed"
"$root/eng/install.sh" "$never_installed" --uninstall >/dev/null
[[ ! -e $never_installed/Directory.Build.props && ! -e $never_installed/Directory.Build.targets ]] || fail 'uninstall created MSBuild files in a never-installed repository'

"$root/eng/sync_skill_assets.sh" --check >/dev/null || fail 'skill assets are not synchronized'
tampered_skill=$temporary_root/install-dotnet-anti-slop
cp -R -- "$root/skills/install-dotnet-anti-slop" "$tampered_skill"
tampered_asset=$tampered_skill/assets/agent-guidance.md
reference_time=$temporary_root/reference-time
touch -r "$tampered_asset" "$reference_time"
first_byte=$(head -c 1 "$tampered_asset")
replacement='!'
[[ $first_byte != '!' ]] || replacement='#'
printf '%s' "$replacement" | dd of="$tampered_asset" bs=1 count=1 conv=notrunc status=none
touch -r "$reference_time" "$tampered_asset"
if "$root/eng/sync_skill_assets.sh" --check --skill-path "$tampered_skill" >/dev/null 2>&1; then
  fail 'skill synchronization accepted same-size, same-mtime tampering'
fi
cp -- "$root/skills/install-dotnet-anti-slop/assets/agent-guidance.md" "$tampered_asset"
sed -i 's#https://github.com/devmobasa/dotnet-anti-slop#https://invalid.example/repository#' "$tampered_skill/assets/provenance.json"
if "$root/eng/sync_skill_assets.sh" --check --skill-path "$tampered_skill" >/dev/null 2>&1; then
  fail 'skill synchronization accepted provenance metadata tampering'
fi
cp -- "$root/skills/install-dotnet-anti-slop/assets/provenance.json" "$tampered_skill/assets/provenance.json"
sed -i -E 's/"source_revision": "[^"]+"/"source_revision": "0000000000000000000000000000000000000000"/' "$tampered_skill/assets/provenance.json"
if "$root/eng/sync_skill_assets.sh" --check --skill-path "$tampered_skill" >/dev/null 2>&1; then
  fail 'skill synchronization accepted an all-zero source revision'
fi

global_json=$temporary_root/global.json
printf '{"sdk":{"version":"99.0.100","rollForward":"disable"}}\n' >"$global_json"
"$root/eng/write_global_json.sh" 8.0.123 --output "$global_json"
jq -e '. == {sdk:{version:"8.0.123",rollForward:"disable",allowPrerelease:false}}' "$global_json" >/dev/null || fail 'global.json writer did not replace an incompatible SDK pin'

legacy_runtime_pattern='py''thon|\.p''y\b|Py''thon'
if rg -n --hidden "$legacy_runtime_pattern" "$root" --glob '!.git/**' --glob '!docs/temp/**' --glob '!**/bin/**' --glob '!**/obj/**' >/dev/null; then
  fail 'legacy scripting dependency or reference remains in the public repository'
fi

printf 'verification passed: %d rules, profiles, docs, tests, structured files, installer, and repository hygiene\n' "$expected_rule_count"
