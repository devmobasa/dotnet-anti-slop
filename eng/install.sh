#!/bin/sh
set -eu

BEGIN_MARKER='<!-- dotnet-anti-slop:begin -->'
END_MARKER='<!-- dotnet-anti-slop:end -->'
SOURCE_REPOSITORY='https://github.com/devmobasa/dotnet-anti-slop'
ZERO_REVISION='0000000000000000000000000000000000000000'

usage() {
  cat <<'EOF'
Usage: install.sh TARGET [--profile PROFILE] [--vendor-dir DIRECTORY]
                         [--force] [--uninstall] [--dry-run]

Vendor dotnet-anti-slop into a .NET repository.

Profiles: default, strict, performance, web-api
EOF
}

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 2
}

command_path() {
  command -v "$1" 2>/dev/null || return 1
}

sha256_stream() {
  if command_path sha256sum >/dev/null; then
    sha256sum | awk '{print $1}'
  elif command_path shasum >/dev/null; then
    shasum -a 256 | awk '{print $1}'
  else
    fail 'sha256sum or shasum is required'
  fi
}

absolute_directory() {
  (CDPATH= cd -- "$1" 2>/dev/null && pwd -P) || return 1
}

script_directory() {
  script_path=$0
  case $script_path in
    /*) ;;
    *) script_path=$PWD/$script_path ;;
  esac
  absolute_directory "$(dirname -- "$script_path")"
}

validate_vendor_dir() {
  case $1 in
    ''|/*|..|../*|*/..|*/../*)
      fail '--vendor-dir must be a non-empty repository-relative path'
      ;;
  esac
}

json_value() {
  json_file=$1
  json_key=$2
  sed -n "s/^[[:space:]]*\"$json_key\"[[:space:]]*:[[:space:]]*\"\{0,1\}\([^\",}]*\)\"\{0,1\}[[:space:]]*,\{0,1\}[[:space:]]*$/\1/p" "$json_file" | head -n 1
}

payload_digest() {
  digest_root=$1
  digest_mode=$2
  digest_manifest=$(mktemp "${TMPDIR:-/tmp}/dotnet-anti-slop-digest.XXXXXX")
  trap 'rm -f -- "$digest_manifest"' EXIT HUP INT TERM

  if [ "$digest_mode" = skill ]; then
    for digest_directory in scripts assets; do
      [ -d "$digest_root/$digest_directory" ] || continue
      find "$digest_root/$digest_directory" -type f ! -name provenance.json \
        ! -path '*/bin/*' ! -path '*/obj/*' ! -path '*/.vs/*' \
        ! -path '*/artifacts/*' -print
    done | while IFS= read -r digest_file; do
      digest_relative=${digest_file#"$digest_root/"}
      printf '%s\t%s\n' "$digest_relative" "$digest_file"
    done >"$digest_manifest"
  else
    printf 'scripts/install.sh\t%s\n' "$digest_root/eng/install.sh" >"$digest_manifest"
    printf 'scripts/install.ps1\t%s\n' "$digest_root/eng/install.ps1" >>"$digest_manifest"
    find "$digest_root/src/DotNetAntiSlop.Analyzers" -type f \
      ! -path '*/bin/*' ! -path '*/obj/*' ! -path '*/.vs/*' \
      ! -path '*/artifacts/*' -print | \
      while IFS= read -r digest_file; do
        digest_relative=${digest_file#"$digest_root/src/DotNetAntiSlop.Analyzers/"}
        printf 'assets/analyzer/%s\t%s\n' "$digest_relative" "$digest_file"
      done >>"$digest_manifest"
    find "$digest_root/config/profiles" -type f -print | while IFS= read -r digest_file; do
      digest_relative=${digest_file#"$digest_root/config/profiles/"}
      printf 'assets/config/profiles/%s\t%s\n' "$digest_relative" "$digest_file"
    done >>"$digest_manifest"
    find "$digest_root/templates" -type f -print | while IFS= read -r digest_file; do
      digest_relative=${digest_file#"$digest_root/templates/"}
      printf 'assets/templates/%s\t%s\n' "$digest_relative" "$digest_file"
    done >>"$digest_manifest"
    printf 'assets/agent-guidance.md\t%s\n' "$digest_root/AGENTS.md" >>"$digest_manifest"
  fi

  LC_ALL=C sort "$digest_manifest" | while IFS="$(printf '\t')" read -r digest_relative digest_file; do
    printf '%s\000' "$digest_relative"
    cat -- "$digest_file"
    printf '\000'
  done | sha256_stream

  rm -f -- "$digest_manifest"
  trap - EXIT HUP INT TERM
}

copy_tree() {
  copy_source=$1
  copy_destination=$2
  mkdir -p -- "$copy_destination"
  cp -R -- "$copy_source/." "$copy_destination/"
  find "$copy_destination" -type d \( -name bin -o -name obj -o -name .vs \
    -o -name artifacts \) -prune -exec rm -rf -- {} +
}

remove_managed_block() {
  managed_path=$1
  managed_output=$2
  awk -v begin="$BEGIN_MARKER" -v end="$END_MARKER" '
    index($0, begin) { inside = 1; found = 1; next }
    inside && index($0, end) { inside = 0; next }
    !inside { lines[++count] = $0 }
    END {
      if (inside) exit 3
      first = 1
      while (first <= count && lines[first] == "") first++
      last = count
      while (last >= first && lines[last] == "") last--
      for (i = first; i <= last; i++) print lines[i]
    }
  ' "$managed_path" >"$managed_output" || fail "found $BEGIN_MARKER without matching $END_MARKER in $managed_path"
}

update_msbuild_import() {
  import_path=$1
  imported_file=$2
  import_mode=$3

  if [ "$import_mode" = uninstall ] && [ ! -f "$import_path" ]; then
    return
  fi

  import_temp=$(mktemp "${TMPDIR:-/tmp}/dotnet-anti-slop-import.XXXXXX")
  import_clean=$(mktemp "${TMPDIR:-/tmp}/dotnet-anti-slop-clean.XXXXXX")
  trap 'rm -f -- "$import_temp" "$import_clean"' EXIT HUP INT TERM

  if [ -f "$import_path" ]; then
    if [ "$import_mode" = uninstall ] && ! grep -Fq "$BEGIN_MARKER" "$import_path"; then
      rm -f -- "$import_temp" "$import_clean"
      trap - EXIT HUP INT TERM
      return
    fi
    remove_managed_block "$import_path" "$import_clean"
  else
    printf '<Project>\n</Project>\n' >"$import_clean"
  fi

  if [ "$import_mode" = install ]; then
    grep -Fq '</Project>' "$import_clean" || fail "$import_path is not an MSBuild project: missing </Project>"
    import_project='$(MSBuildThisFileDirectory)'"$vendor_dir/$imported_file"
    awk -v begin="$BEGIN_MARKER" -v end="$END_MARKER" -v project="$import_project" '
      {
        lines[++count] = $0
        if (index($0, "</Project>")) closing = count
      }
      END {
        if (!closing) exit 3
        prefix = closing - 1
        while (prefix > 0 && lines[prefix] == "") prefix--
        for (i = 1; i <= prefix; i++) print lines[i]
        print "  " begin
        print "    <Import Project=\"" project "\" Condition=\"Exists('\''" project "'\'')\" />"
        print "  " end
        for (i = closing; i <= count; i++) print lines[i]
      }
    ' "$import_clean" >"$import_temp" || fail "$import_path is not an MSBuild project: missing </Project>"
  else
    cp -- "$import_clean" "$import_temp"
  fi

  if [ "$dry_run" = true ]; then
    printf 'would update: %s\n' "$import_path"
  else
    mv -- "$import_temp" "$import_path"
    printf 'updated: %s\n' "$import_path"
  fi

  rm -f -- "$import_temp" "$import_clean"
  trap - EXIT HUP INT TERM
}

shell_quote() {
  quote_value=$(printf '%s' "$1" | sed "s/'/'\\\\''/g")
  printf "'%s'" "$quote_value"
}

script_dir=$(script_directory) || fail 'cannot resolve the installer directory'
if [ -d "$script_dir/../assets/analyzer" ]; then
  source_root=$(absolute_directory "$script_dir/..")
  skill_assets=$source_root/assets
  analyzer_source=$skill_assets/analyzer
  profile_base=$skill_assets/config/profiles
  provenance_mode=skill
else
  source_root=$(absolute_directory "$script_dir/..")
  skill_assets=$source_root/assets
  analyzer_source=$source_root/src/DotNetAntiSlop.Analyzers
  profile_base=$source_root/config/profiles
  provenance_mode=canonical
fi

target=
profile=default
vendor_dir=.dotnet-anti-slop
force=false
uninstall=false
dry_run=false

while [ "$#" -gt 0 ]; do
  case $1 in
    --profile)
      [ "$#" -ge 2 ] || fail '--profile requires a value'
      profile=$2
      shift 2
      ;;
    --vendor-dir)
      [ "$#" -ge 2 ] || fail '--vendor-dir requires a value'
      vendor_dir=$2
      shift 2
      ;;
    --force) force=true; shift ;;
    --uninstall) uninstall=true; shift ;;
    --dry-run) dry_run=true; shift ;;
    -h|--help) usage; exit 0 ;;
    -*) fail "unknown argument: $1" ;;
    *)
      [ -z "$target" ] || fail "unexpected positional argument: $1"
      target=$1
      shift
      ;;
  esac
done

[ -n "$target" ] || { usage >&2; exit 2; }

case $profile in
  default|strict|performance|web-api) ;;
  *) fail "unsupported profile: $profile" ;;
esac
validate_vendor_dir "$vendor_dir"

case $target in
  '~') target=$HOME ;;
  '~/'*) target=$HOME/${target#\~/} ;;
esac
target_root=$(absolute_directory "$target") || fail "target directory does not exist: $target"
vendor_root=$target_root/$vendor_dir
directory_build_props=$target_root/Directory.Build.props
directory_build_targets=$target_root/Directory.Build.targets

if [ "$uninstall" = true ]; then
  update_msbuild_import "$directory_build_props" DotNetAntiSlop.props uninstall
  update_msbuild_import "$directory_build_targets" DotNetAntiSlop.targets uninstall
  if [ -d "$vendor_root" ]; then
    if [ "$dry_run" = true ]; then
      printf 'would remove: %s\n' "$vendor_root"
    else
      rm -rf -- "$vendor_root"
      printf 'removed: %s\n' "$vendor_root"
    fi
  fi
  exit 0
fi

if [ -e "$vendor_root" ] && [ "$force" != true ]; then
  fail "$vendor_root already exists; use --force to refresh it"
fi

profile_source=$profile_base/$profile.globalconfig
[ -d "$analyzer_source" ] && [ -f "$profile_source" ] || fail 'installer source tree is incomplete'

revision=unversioned-source
source_state=unversioned
content_sha256=
provenance_file=$skill_assets/provenance.json

if [ "$provenance_mode" = skill ] && [ -f "$provenance_file" ]; then
  provenance_keys=$(sed -n 's/^[[:space:]]*"\([^"]*\)"[[:space:]]*:.*/\1/p' "$provenance_file" | LC_ALL=C sort)
  expected_keys='content_sha256
schema_version
source_repository
source_revision'
  [ "$provenance_keys" = "$expected_keys" ] || fail 'skill provenance has an invalid schema'
  schema_version=$(json_value "$provenance_file" schema_version)
  repository=$(json_value "$provenance_file" source_repository)
  revision=$(json_value "$provenance_file" source_revision)
  expected_digest=$(json_value "$provenance_file" content_sha256)
  [ "$schema_version" = 1 ] || fail 'skill provenance has an unsupported schema version'
  [ "$repository" = "$SOURCE_REPOSITORY" ] || fail 'skill provenance contains an unexpected source repository'
  if [ "$revision" != unversioned-source ] && ! printf '%s\n' "$revision" | grep -Eq '^[0-9a-f]{40}$'; then
    fail 'skill provenance does not contain a source revision'
  fi
  [ "$revision" != "$ZERO_REVISION" ] || fail 'skill provenance does not contain a source revision'
  printf '%s\n' "$expected_digest" | grep -Eq '^[0-9a-f]{64}$' || fail 'skill provenance does not contain a content digest'
  content_sha256=$(payload_digest "$source_root" skill)
  if [ "$content_sha256" = "$expected_digest" ]; then
    source_state=synchronized-snapshot
  else
    source_state=modified-skill
  fi
else
  git_root=$(git -C "$source_root" rev-parse --show-toplevel 2>/dev/null || true)
  if [ -n "$git_root" ] && [ "$(absolute_directory "$git_root")" = "$source_root" ]; then
    revision=$(git -C "$source_root" rev-parse HEAD 2>/dev/null || printf unversioned-source)
    if git -C "$source_root" status --porcelain -- AGENTS.md config eng/install.sh eng/install.ps1 src/DotNetAntiSlop.Analyzers templates | grep -q .; then
      source_state=dirty
    else
      source_state=clean
    fi
  fi
  content_sha256=$(payload_digest "$source_root" canonical)
fi

if [ "$dry_run" = true ]; then
  printf 'would copy: %s -> %s\n' "$analyzer_source" "$vendor_root/analyzer"
  printf 'would copy: %s -> %s\n' "$profile_source" "$vendor_root/config/$profile.globalconfig"
else
  [ ! -e "$vendor_root" ] || rm -rf -- "$vendor_root"
  mkdir -p -- "$vendor_root/config"
  copy_tree "$analyzer_source" "$vendor_root/analyzer"
  cp -- "$profile_source" "$vendor_root/config/$profile.globalconfig"

  cat >"$vendor_root/DotNetAntiSlop.props" <<EOF
<Project>
  <ItemGroup Condition="'\$(IsDotNetAntiSlopAnalyzerProject)' != 'true'">
    <GlobalAnalyzerConfigFiles
      Include="\$(MSBuildThisFileDirectory)config/$profile.globalconfig" />
  </ItemGroup>
</Project>
EOF

  cat >"$vendor_root/DotNetAntiSlop.targets" <<'EOF'
<Project>
  <ItemGroup Condition="'$(IsDotNetAntiSlopAnalyzerProject)' != 'true'">
    <ProjectReference
      Include="$(MSBuildThisFileDirectory)analyzer/DotNetAntiSlop.Analyzers.csproj"
      OutputItemType="Analyzer"
      ReferenceOutputAssembly="false"
      PrivateAssets="all" />
  </ItemGroup>
</Project>
EOF

  refresh="$(shell_quote "$0") $(shell_quote "$target_root") --profile $profile --force"
  cat >"$vendor_root/INSTALLATION.md" <<EOF
# Vendored dotnet-anti-slop

Profile: \`$profile\`

Source: \`$SOURCE_REPOSITORY\`

Source revision: \`$revision\`

Source state: \`$source_state\`

Source content SHA-256: \`$content_sha256\`

Refresh from the same policy checkout or installed skill with:

\`$refresh\`

Compare the vendored directory with the source before forcing a refresh; local analyzer changes are owned by this repository.
EOF
  printf 'installed: %s\n' "$vendor_root"
fi

update_msbuild_import "$directory_build_props" DotNetAntiSlop.props install
update_msbuild_import "$directory_build_targets" DotNetAntiSlop.targets install
