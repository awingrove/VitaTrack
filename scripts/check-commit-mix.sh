#!/usr/bin/env bash
# Commit-mix freeze trigger (docs/factory/new-shard.md, "Freeze (agreed 2026-09-27)").
# A branch whose non-product commits outnumber its product commits is not a product
# branch — stop and ask what the ratio is doing.
#
# Counts plain AGENTS.md prefixes on BASE..HEAD: product = feat/fix/refactor,
# non-product = test/docs/chore (the freeze's "tests, docs, guardrails").
#
# Emits a GitHub Actions ::warning:: annotation and ALWAYS exits 0 — warning by
# design, not a gate: the freeze's remedy is a human ruling recorded in the PR
# body, and automation cannot tell feature-test commits (product quality) from
# meta bookkeeping — the carve-out new-shard.md leaves open. A hard gate here
# would have blocked legitimate product branches.
set -euo pipefail

BASE_REF="${1:-origin/main}"

if ! git rev-parse --verify --quiet "${BASE_REF}" >/dev/null; then
  echo "::warning title=Commit-mix check skipped::Base ref '${BASE_REF}' not found — freeze trigger not evaluated."
  exit 0
fi

product=0
nonproduct=0
while IFS= read -r subject; do
  case "${subject}" in
    feat:*|fix:*|refactor:*) product=$((product + 1));;
    test:*|docs:*|chore:*)   nonproduct=$((nonproduct + 1));;
  esac
done < <(git log --format=%s "${BASE_REF}..HEAD")

if (( nonproduct > product )); then
  echo "::warning title=Commit-mix freeze triggered::${nonproduct} non-product (test/docs/chore) vs ${product} product (feat/fix/refactor) commits on ${BASE_REF}..HEAD. new-shard.md: stop and ask what the ratio is doing — record the ruling in the PR body."
fi
