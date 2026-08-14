#!/usr/bin/env bash
set -euo pipefail

# Non-interactive when CI=true. If an API key is already in the env
# (e.g. local shell with VitaTrack__ApiKey exported, or CI secret), use
# it. Otherwise: prompt locally, or export empty in CI (the LLM
# integration test self-skips on empty key).
if [[ -z "${VitaTrack__ApiKey:-}" ]]; then
    if [[ -n "${CI:-}" ]]; then
        export VitaTrack__ApiKey=""
    else
        echo "LLM API key (leave blank to skip LLM integration test):"
        read -rsp "> " LLM_API_KEY
        echo
        export VitaTrack__ApiKey="$LLM_API_KEY"
    fi
fi

cd e2e-tests/playwright
exec npx playwright test