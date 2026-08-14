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
#   2. `VitaTrack.ArchitectureTests` only — fast (~1s), catches the rules
#      csproj can't express (controllers must not depend on data layers,
#      EF Core transitive banned, repo naming, 300-line file-size gate,
#      no `catch (Exception)` in controllers). The full unit test suite is
#      intentionally skipped here so the hook stays sub-second.
#
# Bypass for a noisy commit in flight: `git commit --no-verify`.

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

echo "[pre-commit] dotnet format --verify-no-changes ..."
dotnet format VitaTrack.sln --verify-no-changes --no-restore 1>/dev/null

echo "[pre-commit] ArchitectureTests ..."
dotnet test VitaTrack.ArchitectureTests/VitaTrack.ArchitectureTests.csproj \
    --no-restore --nologo --verbosity quiet 1>/dev/null

echo "[pre-commit] OK"