#!/usr/bin/env bash
# CI gate: fail if `dotnet format` would change anything.
# Exits 0 if code is formatted, 1 if not.
set -euo pipefail

echo "Running dotnet format --verify-no-changes ..."
dotnet format VitaTrack.sln --verify-no-changes --no-restore

echo "OK: no formatting changes required."