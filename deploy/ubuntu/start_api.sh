#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LOG_DIR="$ROOT_DIR/.runtime/logs"
PID_DIR="$ROOT_DIR/.runtime/pids"
API_LOG="$LOG_DIR/api.log"
API_PID="$PID_DIR/api.pid"

mkdir -p "$LOG_DIR" "$PID_DIR"

cd "$ROOT_DIR"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required on the host to run the API" >&2
  exit 1
fi

if [[ -f "$API_PID" ]] && kill -0 "$(cat "$API_PID")" 2>/dev/null; then
  echo "API is already running with PID $(cat "$API_PID")"
  exit 0
fi

nohup env \
  ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:5000}" \
  ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Local}" \
  dotnet run --project "$ROOT_DIR/src/AiCan.Api/AiCan.Api.csproj" \
  >"$API_LOG" 2>&1 &

echo $! >"$API_PID"
echo "API started with PID $(cat "$API_PID")"
echo "Log: $API_LOG"
