#!/usr/bin/env bash
# Deploy and restart AiCan API on Linux.

set -e
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
API_LOG="$PROJECT_ROOT/.runtime/logs/api.log"
DOTNET_BIN="${DOTNET_BIN:-}"

if [[ -z "$DOTNET_BIN" ]]; then
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET_BIN="$(command -v dotnet)"
  elif [[ -x "/opt/homebrew/opt/dotnet@8/libexec/dotnet" ]]; then
    DOTNET_BIN="/opt/homebrew/opt/dotnet@8/libexec/dotnet"
  fi
fi

if [[ -z "$DOTNET_BIN" || ! -x "$DOTNET_BIN" ]]; then
  echo "dotnet is required on the host to deploy the API. Set DOTNET_BIN if it is installed outside PATH." >&2
  exit 1
fi

echo "=== AiCan API deploy ==="

# 1. Stop any running API
if fuser 5080/tcp &>/dev/null; then
  echo "Stopping running API on port 5080..."
  fuser -k 5080/tcp || true
  sleep 3
fi

# 2. Build
echo "Building AiCan.Api..."
cd "$PROJECT_ROOT"
touch src/AiCan.Api/Services.cs
"$DOTNET_BIN" build src/AiCan.Api/AiCan.Api.csproj -c Release --nologo -v q

# 3. Start
mkdir -p .runtime/logs
echo "Starting API..."
nohup "$DOTNET_BIN" src/AiCan.Api/bin/Release/net8.0/AiCan.Api.dll \
  --urls http://0.0.0.0:5080 \
  > "$API_LOG" 2>&1 &

sleep 4
echo "Checking health..."
curl -s http://127.0.0.1:5080/healthz && echo " OK" || echo " FAILED - check $API_LOG"
echo "=== Done ==="
