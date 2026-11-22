#!/usr/bin/env bash
# Builds and runs the harness with a throw-away settings folder, so the plugin's settings files never touch the real ones.
set -e
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
dotnet build -c Release -p:LinkPlugin=false 2>&1 | grep -E " error |Build succeeded|FAILED" || true
FAKE_HOME="$(mktemp -d /tmp/workhealth-harness-XXXXXX)"
trap 'rm -rf "$FAKE_HOME"' EXIT
WORKHEALTH_DATA_DIR="$FAKE_HOME" ./bin/Release/net10.0/Harness
