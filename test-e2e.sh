#!/usr/bin/env bash
set -euo pipefail

# Non-interactive when CI=true. The app takes its connection state from the
# database, not the environment, so this variable is only what the LLM integration
# spec reads to decide whether it can reach a real provider: use it if a key is
# already exported (e.g. a local shell, or a CI secret), otherwise prompt
# locally, or export empty in CI (the spec self-skips on an empty key).
if [[ -z "${LLM_API_KEY:-}" ]]; then
    if [[ -n "${CI:-}" ]]; then
        export LLM_API_KEY=""
    else
        echo "LLM API key (leave blank to skip LLM integration test):"
        read -rsp "> " LLM_API_KEY
        echo
        export LLM_API_KEY
    fi
fi

cd e2e-tests/playwright
exec npx playwright test