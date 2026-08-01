#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SETTINGS="$SCRIPT_DIR/VitaTrack.Web/appsettings.json"

# Read a value from the Llm section of appsettings.json
read_setting() {
    local key="$1"
    grep -o "\"$key\": *\"[^\"]*\"" "$SETTINGS" 2>/dev/null | sed "s/\"$key\": *\"//" | sed 's/"$//' || true
}

# Set a value in the Llm section of appsettings.json
write_setting() {
    local key="$1"
    local value="$2"
    if grep -q "\"$key\":" "$SETTINGS" 2>/dev/null; then
        sed -i "s|\"$key\": *\"[^\"]*\"|\"$key\": \"$value\"|" "$SETTINGS"
    else
        # Key doesn't exist yet — insert it before the closing brace of "Llm"
        sed -i "/\"Llm\": {/a\\    \"$key\": \"$value\"," "$SETTINGS"
    fi
}

base_url="${Llm__BaseUrl:-$(read_setting BaseUrl)}"
api_key="${Llm__ApiKey:-$(read_setting ApiKey)}"
model="${Llm__Model:-$(read_setting Model)}"

if [[ -z "$base_url" || -z "$api_key" ]]; then
    echo ""
    echo "=== LLM Configuration ==="
    echo "VitaTrack can use any OpenAI-compatible LLM API to extract"
    echo "nutrients from supplement product pages. This is optional —"
    echo "the app works fully without it (you can enter nutrients manually)."
    echo ""

    if [[ -z "$base_url" ]]; then
        echo "Examples:"
        echo "  OpenRouter:  https://openrouter.ai/api/v1"
        echo "  OpenAI:      https://api.openai.com/v1"
        echo "  Local:       http://localhost:11434/v1"
        echo ""
        read -rp "LLM API base URL (leave blank to skip): " base_url
    fi

    if [[ -n "$base_url" && -z "$api_key" ]]; then
        read -rsp "API key: " api_key
        echo ""
    fi

    if [[ -z "$model" || "$model" == "gpt-4o-mini" ]]; then
        read -rp "Model name [gpt-4o-mini]: " input_model
        model="${input_model:-gpt-4o-mini}"
    fi

    if [[ -n "$base_url" && -n "$api_key" ]]; then
        write_setting BaseUrl "$base_url"
        write_setting ApiKey "$api_key"
        write_setting Model "$model"
        echo "LLM config saved to appsettings.json"
    else
        echo "Skipping LLM configuration — enrichment will be disabled."
    fi
    echo ""
fi

dotnet run --project "$SCRIPT_DIR/VitaTrack.Web" --urls http://localhost:5000
