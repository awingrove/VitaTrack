#!/usr/bin/env bash
# Lightweight pre-commit hook for VitaTrack.
#
# Install (once, after clone):
#   cp scripts/install-pre-commit-hook.sh /dev/stdin | sh   # OR:
#   ./scripts/install-pre-commit-hook.sh
#
# What it runs:
#   1. `dotnet format --verify-no-changes` — block if files need reformatting.
#      Run `dotnet format VitaTrack.sln` locally to auto-fix, then re-stage.
#   2. `dotnet test` on ArchitectureTests + the full unit suite (~1s total
#      with incremental build) — catches architecture violations AND
#      behavioral regressions before they land.
#
# Bypass for a noisy commit in flight: `git commit --no-verify`.

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

echo "[pre-commit] dotnet format --verify-no-changes ..."
dotnet format VitaTrack.sln --verify-no-changes --no-restore 1>/dev/null

echo "[pre-commit] tests (architecture + unit) ..."
dotnet test VitaTrack.sln --no-restore --nologo --verbosity quiet 1>/dev/null

echo "[pre-commit] OK"