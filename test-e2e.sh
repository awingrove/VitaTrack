#!/usr/bin/env bash
set -euo pipefail

echo "LLM API key (leave blank to skip LLM integration test):"
read -rsp "> " LLM_API_KEY
echo

export VitaTrack__ApiKey="$LLM_API_KEY"
cd e2e-tests/playwright
npx playwright test
