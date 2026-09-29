#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# There is nothing to configure before the app starts. A provider's base URL, API
# key and model live in the app's own database and are entered in the running app
# at nav bar -> Settings. This script used to prompt for them and write a
# "VitaTrack" block into appsettings.json; that binding is gone, and the write
# anchored on the block's opening line, so the prompt still ran, the sed silently
# wrote nothing, and the script reported a save that had not happened.
echo "Starting VitaTrack. Connect a provider afterwards at http://localhost:5000 -> Settings."

dotnet run --project "$SCRIPT_DIR/VitaTrack.Web" --urls http://localhost:5000
