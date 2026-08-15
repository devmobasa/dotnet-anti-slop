#!/bin/sh
set -eu

usage() {
  printf 'Usage: write_global_json.sh VERSION [--allow-prerelease] [--output PATH]\n'
}

[ "$#" -gt 0 ] || { usage >&2; exit 2; }
version=$1
shift
allow_prerelease=false
output=global.json

while [ "$#" -gt 0 ]; do
  case $1 in
    --allow-prerelease)
      allow_prerelease=true
      shift
      ;;
    --output)
      [ "$#" -ge 2 ] || { printf 'error: --output requires a value\n' >&2; exit 2; }
      output=$2
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      printf 'error: unknown argument: %s\n' "$1" >&2
      exit 2
      ;;
  esac
done

case $version in
  *[!0-9A-Za-z.+-]*|'')
    printf 'error: invalid SDK version: %s\n' "$version" >&2
    exit 2
    ;;
esac

output_directory=$(dirname -- "$output")
mkdir -p -- "$output_directory"
printf '{\n  "sdk": {\n    "version": "%s",\n    "rollForward": "disable",\n    "allowPrerelease": %s\n  }\n}\n' \
  "$version" "$allow_prerelease" >"$output"
