#!/usr/bin/env bash
# Deploy and restart AiCan API on Ubuntu.
# Run this from the Ubuntu machine at /home/joyat/projects/aican

set -e
PROJECT_ROOT="/home/joyat/projects/aican"
API_LOG="$PROJECT_ROOT/.runtime/logs/api.log"

echo "=== AiCan API deploy ==="

# 1. Stop any running API
if fuser 5000/tcp &>/dev/null; then
  echo "Stopping running API on port 5000..."
  fuser -k 5000/tcp || true
  sleep 3
fi

# 2. Build
echo "Building AiCan.Api..."
cd "$PROJECT_ROOT"
touch src/AiCan.Api/Services.cs
dotnet build src/AiCan.Api/AiCan.Api.csproj -c Release --nologo -v q

# 3. Start
mkdir -p .runtime/logs
echo "Starting API..."
nohup dotnet src/AiCan.Api/bin/Release/net8.0/AiCan.Api.dll \
  --urls http://0.0.0.0:5000 \
  > "$API_LOG" 2>&1 &

sleep 4
echo "Checking health..."
curl -s http://100.97.72.86:5000/healthz && echo " OK" || echo " FAILED — check $API_LOG"
echo "=== Done ==="
