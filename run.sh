#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SETTINGS="$SCRIPT_DIR/VitaTrack.Web/appsettings.json"

# Read current LLM config values
read_config() {
    python3 -c "
import json, sys
with open('$SETTINGS') as f:
    cfg = json.load(f)
llm = cfg.get('Llm', {})
print(llm.get('BaseUrl', ''))
print(llm.get('ApiKey', ''))
print(llm.get('Model', 'gpt-4o-mini'))
"
}

# Write LLM config values back to appsettings.json
write_config() {
    local base_url="$1"
    local api_key="$2"
    local model="$3"
    python3 -c "
import json, sys
with open('$SETTINGS') as f:
    cfg = json.load(f)
cfg['Llm'] = {
    'BaseUrl': sys.argv[1],
    'ApiKey': sys.argv[2],
    'Model': sys.argv[3]
}
with open('$SETTINGS', 'w') as f:
    json.dump(cfg, f, indent=2)
    f.write('\n')
" "$base_url" "$api_key" "$model"
}

# Check if LLM config is complete
config=$(read_config)
base_url=$(echo "$config" | sed -n '1p')
api_key=$(echo "$config" | sed -n '2p')
model=$(echo "$config" | sed -n '3p')

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
        echo "  Local (Ollama, etc.): http://localhost:11434/v1"
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
        write_config "$base_url" "$api_key" "$model"
        echo "LLM config saved to appsettings.json"
    else
        echo "Skipping LLM configuration — enrichment will be disabled."
    fi
    echo ""
fi

dotnet run --project "$SCRIPT_DIR/VitaTrack.Web" --urls http://localhost:5000
