#!/usr/bin/env bash
# Lightweight pre-commit hook for VitaTrack.
#
# Install (once, after clone):
#   cp scripts/install-pre-commit-hook.sh /dev/stdin | sh   # OR:
#   ./scripts/install-pre-commit-hook.sh
#
# What it runs:
#   0. Heads-up (never blocks): local `main` ahead of `origin/main` — the
#      remote rejects direct pushes to main, so unpushed main commits are
#      debt that must later ride a PR branch. Rescue recipe included.
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

if [ "$(git branch --show-current)" = "main" ] \
  && git rev-parse --verify --quiet origin/main >/dev/null \
  && [ "$(git rev-list --count origin/main..HEAD)" -gt 0 ]; then
  echo "[pre-commit] WARNING: local main is ahead of origin/main by $(git rev-list --count origin/main..HEAD) commit(s)."
  echo "[pre-commit] Remote rejects direct pushes to main — every one of these must ride a PR branch."
  echo "[pre-commit] Rescue: git branch <name> && git reset --hard origin/main && git checkout <name>"
fi

echo "[pre-commit] dotnet format --verify-no-changes ..."
dotnet format VitaTrack.sln --verify-no-changes --no-restore 1>/dev/null

echo "[pre-commit] tests (architecture + unit) ..."
dotnet test VitaTrack.sln --no-restore --nologo --verbosity quiet 1>/dev/null

echo "[pre-commit] OK"