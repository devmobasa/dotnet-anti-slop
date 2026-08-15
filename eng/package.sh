#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
root=$(CDPATH= cd -- "$script_dir/.." && pwd -P)
output="$root/../dotnet-anti-slop.zip"

while (( $# > 0 )); do
  case $1 in
    --output)
      (( $# >= 2 )) || { printf 'error: --output requires a value\n' >&2; exit 2; }
      output=$2
      shift 2
      ;;
    -h|--help)
      printf 'Usage: package.sh [--output PATH]\n'
      exit 0
      ;;
    *)
      printf 'error: unknown argument: %s\n' "$1" >&2
      exit 2
      ;;
  esac
done

if [[ $output != /* ]]; then
  output=$PWD/$output
fi
output_directory=$(dirname -- "$output")
mkdir -p -- "$output_directory"
checksum_path=$output.sha256
staging=$(mktemp -d "${TMPDIR:-/tmp}/dotnet-anti-slop-package.XXXXXX")
cleanup() {
  rm -rf -- "$staging"
}
trap cleanup EXIT HUP INT TERM
mkdir -p -- "$staging/dotnet-anti-slop"

file_count=0
while IFS= read -r -d '' relative; do
  case /$relative/ in
    */.git/*|*/.vs/*|*/.idea/*|*/bin/*|*/obj/*|*/artifacts/*|*/TestResults/*|*/docs/temp/*)
      continue
      ;;
  esac
  source_file=$root/$relative
  [[ ! -L $source_file ]] || {
    printf 'error: tracked symbolic links are not supported: %s\n' "$relative" >&2
    exit 2
  }
  [[ -f $source_file ]] || continue
  destination=$staging/dotnet-anti-slop/$relative
  mkdir -p -- "$(dirname -- "$destination")"
  cp -p -- "$source_file" "$destination"
  touch -t 202601010000.00 "$destination"
  ((file_count += 1))
done < <(git -C "$root" ls-files -z)

archive_temp=$staging/archive.zip
(
  cd "$staging"
  LC_ALL=C find dotnet-anti-slop -type f -print | LC_ALL=C sort | zip -X -9 -q "$archive_temp" -@
)
mv -- "$archive_temp" "$output"

if command -v sha256sum >/dev/null 2>&1; then
  checksum=$(sha256sum "$output" | awk '{print $1}')
else
  checksum=$(shasum -a 256 "$output" | awk '{print $1}')
fi
printf '%s  %s\n' "$checksum" "$(basename -- "$output")" >"$checksum_path"
printf 'created %s with %d files\n' "$output" "$file_count"
printf 'created %s\n' "$checksum_path"
