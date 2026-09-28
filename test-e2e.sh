#!/usr/bin/env bash
set -euo pipefail

# Non-interactive when CI=true.
#
# The app takes its connection state from the database, not the environment, so these
# three are not configuration the app reads. They are the *input* the real-provider spec
# types into the Settings form, the same way a user would, and then it lets the app's own
# probe decide whether the connection verifies. All three or none: an enrichment with no
# model is refused by the app rather than guessed at, and a base URL has no default left
# to fall back on.
#
# Use them if they are already exported (a local shell, or a CI secret), otherwise prompt
# locally, or export all three empty in CI — the spec self-skips when any is missing, and
# every other spec in the suite is local and runs either way.
if [[ -n "${CI:-}" ]]; then
    export LLM_API_KEY="${LLM_API_KEY:-}"
    export LLM_BASE_URL="${LLM_BASE_URL:-}"
    export LLM_MODEL="${LLM_MODEL:-}"
elif [[ -z "${LLM_API_KEY:-}" ]]; then
    echo "LLM API key (leave blank to skip the real-provider tests):"
    read -rsp "> " LLM_API_KEY
    echo
    export LLM_API_KEY
    echo "Provider base URL, including the version path (e.g. https://gateway.example/v1):"
    read -r "> " LLM_BASE_URL
    export LLM_BASE_URL
    echo "Model that provider expects (e.g. kimi-k2.7-code):"
    read -r "> " LLM_MODEL
    export LLM_MODEL
fi

cd e2e-tests/playwright
exec npx playwright test
