#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LOG_DIR="$ROOT_DIR/.runtime/logs"
PID_DIR="$ROOT_DIR/.runtime/pids"
API_LOG="$LOG_DIR/api.log"
API_PID="$PID_DIR/api.pid"
DOTNET_BIN="${DOTNET_BIN:-}"

mkdir -p "$LOG_DIR" "$PID_DIR"

cd "$ROOT_DIR"

if [[ -z "$DOTNET_BIN" ]]; then
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET_BIN="$(command -v dotnet)"
  elif [[ -x "/opt/homebrew/opt/dotnet@8/libexec/dotnet" ]]; then
    DOTNET_BIN="/opt/homebrew/opt/dotnet@8/libexec/dotnet"
  fi
fi

if [[ -z "$DOTNET_BIN" || ! -x "$DOTNET_BIN" ]]; then
  echo "dotnet is required on the host to run the API. Set DOTNET_BIN if it is installed outside PATH." >&2
  exit 1
fi

if [[ -f "$API_PID" ]] && kill -0 "$(cat "$API_PID")" 2>/dev/null; then
  echo "API is already running with PID $(cat "$API_PID")"
  exit 0
fi

BUILD_CONFIG="${BUILD_CONFIG:-Debug}"
API_DLL="$ROOT_DIR/src/AiCan.Api/bin/$BUILD_CONFIG/net8.0/AiCan.Api.dll"

echo "Building API ($BUILD_CONFIG)..."
"$DOTNET_BIN" build "$ROOT_DIR/src/AiCan.Api/AiCan.Api.csproj" -c "$BUILD_CONFIG" --nologo -v q >/dev/null

nohup env \
  ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:5000}" \
  ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Local}" \
  "$DOTNET_BIN" "$API_DLL" \
  >"$API_LOG" 2>&1 &

echo $! >"$API_PID"
echo "API started with PID $(cat "$API_PID")"
echo "Log: $API_LOG"
