#!/usr/bin/env bash
set -euo pipefail

echo "OpenRouter API key (leave blank to skip LLM integration test):"
read -rsp "> " OPENROUTER_API_KEY
echo

export OpenRouter__ApiKey="$OPENROUTER_API_KEY"
cd e2e-tests/playwright
npx playwright test
