#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
root=$(CDPATH= cd -- "$script_dir/.." && pwd -P)
skill=$root/skills/install-dotnet-anti-slop
check=false
source_repository=https://github.com/devmobasa/dotnet-anti-slop

while (( $# > 0 )); do
  case $1 in
    --check) check=true; shift ;;
    --skill-path)
      (( $# >= 2 )) || { printf 'error: --skill-path requires a value\n' >&2; exit 2; }
      skill=$2
      shift 2
      ;;
    -h|--help)
      printf 'Usage: sync_skill_assets.sh [--check] [--skill-path PATH]\n'
      exit 0
      ;;
    *)
      printf 'error: unknown argument: %s\n' "$1" >&2
      exit 2
      ;;
  esac
done

if [[ $skill != /* ]]; then
  skill=$PWD/$skill
fi

sha256_stream() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum | awk '{print $1}'
  else
    shasum -a 256 | awk '{print $1}'
  fi
}

payload_digest() {
  local payload_root=$1
  local manifest
  manifest=$(mktemp "${TMPDIR:-/tmp}/dotnet-anti-slop-payload.XXXXXX")
  for directory in scripts assets; do
    [[ -d $payload_root/$directory ]] || continue
    find "$payload_root/$directory" -type f ! -name provenance.json -print
  done | while IFS= read -r payload_file; do
    printf '%s\t%s\n' "${payload_file#"$payload_root/"}" "$payload_file"
  done >"$manifest"

  LC_ALL=C sort "$manifest" | while IFS=$'\t' read -r relative payload_file; do
    printf '%s\0' "$relative"
    command cat -- "$payload_file"
    printf '\0'
  done | sha256_stream
  unlink "$manifest"
}

copy_tree() {
  local source=$1
  local destination=$2
  mkdir -p -- "$destination"
  cp -R -- "$source/." "$destination/"
  find "$destination" -type d \( -name bin -o -name obj -o -name .vs \
    -o -name artifacts \) -prune -exec rm -rf -- {} +
}

source_revision() {
  local git_root revision
  git_root=$(git -C "$root" rev-parse --show-toplevel 2>/dev/null || true)
  [[ -n $git_root && $(CDPATH= cd -- "$git_root" && pwd -P) == "$root" ]] || {
    printf 'unversioned-source\n'
    return
  }
  revision=$(git -C "$root" rev-parse HEAD 2>/dev/null || true)
  [[ -n $revision ]] || {
    printf 'unversioned-source\n'
    return
  }
  if git -C "$root" status --porcelain -- AGENTS.md config eng/install.sh eng/install.ps1 \
    eng/sync_skill_assets.sh src/DotNetAntiSlop.Analyzers templates | grep -q .; then
    printf 'unversioned-source\n'
  else
    printf '%s\n' "$revision"
  fi
}

populate() {
  local destination=$1 revision digest
  mkdir -p -- "$destination/scripts" "$destination/assets/config"
  cp -- "$root/eng/install.sh" "$destination/scripts/install.sh"
  cp -- "$root/eng/install.ps1" "$destination/scripts/install.ps1"
  copy_tree "$root/src/DotNetAntiSlop.Analyzers" "$destination/assets/analyzer"
  copy_tree "$root/config/profiles" "$destination/assets/config/profiles"
  copy_tree "$root/templates" "$destination/assets/templates"
  cp -- "$root/AGENTS.md" "$destination/assets/agent-guidance.md"
  revision=$(source_revision)
  digest=$(payload_digest "$destination")
  printf '{\n  "schema_version": 1,\n  "source_repository": "%s",\n  "source_revision": "%s",\n  "content_sha256": "%s"\n}\n' \
    "$source_repository" "$revision" "$digest" >"$destination/assets/provenance.json"
}

generated=$(mktemp -d "${TMPDIR:-/tmp}/dotnet-anti-slop-skill.XXXXXX")
cleanup() {
  rm -rf -- "$generated"
}
trap cleanup EXIT HUP INT TERM
populate "$generated"

if [[ $check == true ]]; then
  differences=$(diff -qr --exclude=SKILL.md --exclude=agents --exclude=provenance.json "$generated" "$skill" || true)
  expected_digest=$(payload_digest "$generated")
  actual_digest=$(payload_digest "$skill")
  provenance=$skill/assets/provenance.json
  provenance_ok=true
  [[ -f $provenance ]] || provenance_ok=false
  if [[ $provenance_ok == true ]]; then
    jq -e --arg repository "$source_repository" --arg digest "$expected_digest" '
      (keys | sort) == ["content_sha256", "schema_version", "source_repository", "source_revision"] and
      .schema_version == 1 and
      .source_repository == $repository and
      .content_sha256 == $digest and
      (.source_revision | type == "string") and
      (.source_revision | test("^([0-9a-f]{40}|unversioned-source)$")) and
      .source_revision != "0000000000000000000000000000000000000000"
    ' "$provenance" >/dev/null || provenance_ok=false
  fi
  [[ $actual_digest == "$expected_digest" ]] || provenance_ok=false

  if [[ -n $differences || $provenance_ok != true ]]; then
    printf 'skill assets are out of sync:\n'
    [[ -z $differences ]] || printf '%s\n' "$differences" | sed 's/^/  /'
    [[ $provenance_ok == true ]] || printf '  %s\n' "$provenance"
    exit 1
  fi
  printf 'skill assets are synchronized\n'
  exit 0
fi

for name in scripts assets; do
  if [[ -e $skill/$name ]]; then
    rm -rf -- "$skill/$name"
  fi
  cp -R -- "$generated/$name" "$skill/$name"
done
printf 'synchronized skill assets: %s\n' "$skill"
