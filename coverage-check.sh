#!/usr/bin/env bash
# Run unit tests with coverage and gate at the current coverage floor.
# Floor is intentionally set below the aspirational AGENTS.md target (≥80%
# on Infrastructure) because the codebase has not yet reached it (Aug 2026:
# Infra ~53%). Gate prevents regression; raise the threshold here as
# targeted test PRs ratchet coverage up toward 80%.
set -euo pipefail

THRESHOLD="${COVERAGE_THRESHOLD:-50}"
THRESHOLD_TYPE="${COVERAGE_THRESHOLD_TYPE:-line}"
CONFIGURATION="${CONFIGURATION:-Debug}"

mkdir -p TestResults/coverage

dotnet test VitaTrack.Tests/VitaTrack.Tests.csproj --configuration "$CONFIGURATION" --no-build \
    -p:CollectCoverage=true \
    -p:Threshold="$THRESHOLD" \
    -p:ThresholdType="$THRESHOLD_TYPE" \
    -p:ThresholdStat=total \
    -p:Exclude="[VitaTrack.Web]*" \
    -p:CoverletOutputFormat=cobertura \
    -p:CoverletOutput=../TestResults/coverage/

echo
echo "Coverage report: TestResults/coverage/coverage.cobertura.xml"
echo "Floor: $THRESHOLD% ($THRESHOLD_TYPE). Raise via COVERAGE_THRESHOLD=NN ./coverage-check.sh."