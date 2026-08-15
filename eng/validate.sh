#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)

"$SCRIPT_DIR/verify_repo.sh"
dotnet restore "$ROOT/dotnet-anti-slop.slnx"
dotnet build "$ROOT/dotnet-anti-slop.slnx" -c Release --no-restore
dotnet test "$ROOT/dotnet-anti-slop.slnx" -c Release --no-build
