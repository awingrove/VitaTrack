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
# locally for whichever are missing, or export all three empty in CI — the spec self-skips
# when any is missing, and every other spec in the suite is local and runs either way.
if [[ -n "${CI:-}" ]]; then
    export LLM_API_KEY="${LLM_API_KEY:-}"
    export LLM_BASE_URL="${LLM_BASE_URL:-}"
    export LLM_MODEL="${LLM_MODEL:-}"
elif [[ -z "${LLM_API_KEY:-}" || -z "${LLM_BASE_URL:-}" || -z "${LLM_MODEL:-}" ]]; then
    # One prompt per variable that is actually missing, not one prompt for the key that
    # then asks for two things it cannot have. A developer with a key already exported
    # and no base URL used to get no prompt at all, and a skip naming two things missing.
    if [[ -z "${LLM_API_KEY:-}" ]]; then
        echo "LLM API key (leave blank to skip the real-provider tests):"
        read -rsp "> " LLM_API_KEY
        echo
    fi
    if [[ -z "${LLM_BASE_URL:-}" ]]; then
        # The gateway root, WITHOUT a version segment. The app appends its own
        # "v1/models" (ServiceEndpoint.Resolve), so a base that already ends in /v1
        # asks for /v1/v1/models, comes back 404, and leaves the connection unverified
        # with a note blaming an endpoint that never got the question. A path is fine
        # here — "https://gateway.example/openai" verifies — a version segment is not.
        echo "Provider base URL, gateway root with no /v1 on the end (e.g. https://gateway.example):"
        read -r "> " LLM_BASE_URL
    fi
    if [[ -z "${LLM_MODEL:-}" ]]; then
        echo "Model that provider expects (e.g. kimi-k2.7-code):"
        read -r "> " LLM_MODEL
    fi
    export LLM_API_KEY LLM_BASE_URL LLM_MODEL
fi

cd e2e-tests/playwright
exec npx playwright test
