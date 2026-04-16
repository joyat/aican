#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VENV_DIR="$ROOT_DIR/.venv/worker"
LOG_DIR="$ROOT_DIR/.runtime/logs"
PID_DIR="$ROOT_DIR/.runtime/pids"
WORKER_LOG="$LOG_DIR/worker.log"
WORKER_PID="$PID_DIR/worker.pid"

mkdir -p "$LOG_DIR" "$PID_DIR"

cd "$ROOT_DIR"

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required on the host to run the worker" >&2
  exit 1
fi

if [[ ! -d "$VENV_DIR" ]]; then
  python3 -m venv "$VENV_DIR"
fi

source "$VENV_DIR/bin/activate"
pip install --upgrade pip >/dev/null
pip install -r "$ROOT_DIR/workers/ai_worker/requirements.txt" >/dev/null

if [[ -f "$WORKER_PID" ]] && kill -0 "$(cat "$WORKER_PID")" 2>/dev/null; then
  echo "Worker is already running with PID $(cat "$WORKER_PID")"
  exit 0
fi

nohup "$VENV_DIR/bin/python" -m uvicorn workers.ai_worker.main:app \
  --app-dir "$ROOT_DIR" \
  --host 0.0.0.0 \
  --port 8001 \
  >"$WORKER_LOG" 2>&1 &

echo $! >"$WORKER_PID"
echo "Worker started with PID $(cat "$WORKER_PID")"
echo "Log: $WORKER_LOG"
