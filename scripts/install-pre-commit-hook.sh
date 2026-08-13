#!/usr/bin/env bash
# Install pre-commit hook for VitaTrack.
# Symlinks scripts/pre-commit.sh into .git/hooks/pre-commit.
# Re-run after clone or after `git init` to set up.
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
SOURCE="$REPO_ROOT/scripts/pre-commit.sh"
TARGET="$REPO_ROOT/.git/hooks/pre-commit"

mkdir -p "$(dirname "$TARGET")"
chmod +x "$SOURCE"

ln -sf "$SOURCE" "$TARGET"

echo "Installed pre-commit hook:"
echo "  $TARGET -> $SOURCE"
echo
echo "To bypass on a specific commit: git commit --no-verify"