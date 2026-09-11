#!/usr/bin/env bash
# Coverage floor set at 90% line coverage.
set -euo pipefail

THRESHOLD="${COVERAGE_THRESHOLD:-90}"
THRESHOLD_TYPE="${THRESHOLD_TYPE:-line}"
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