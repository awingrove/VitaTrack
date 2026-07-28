#!/usr/bin/env bash
set -euo pipefail
cd e2e-tests/playwright
npx playwright test
