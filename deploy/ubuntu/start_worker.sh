#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VENV_DIR="$ROOT_DIR/.venv/worker"
LOG_DIR="$ROOT_DIR/.runtime/logs"
PID_DIR="$ROOT_DIR/.runtime/pids"
WORKER_LOG="$LOG_DIR/worker.log"
WORKER_PID="$PID_DIR/worker.pid"
PYTHON_BIN="${PYTHON_BIN:-}"

mkdir -p "$LOG_DIR" "$PID_DIR"

cd "$ROOT_DIR"

if [[ -z "$PYTHON_BIN" ]]; then
  for candidate in python3.12 python3.11 python3.10 python3; do
    if command -v "$candidate" >/dev/null 2>&1; then
      PYTHON_BIN="$(command -v "$candidate")"
      break
    fi
  done
fi

if [[ -z "$PYTHON_BIN" ]]; then
  echo "Python 3.10+ is required on the host to run the worker. Set PYTHON_BIN if it is installed outside PATH." >&2
  exit 1
fi

if ! "$PYTHON_BIN" - <<'PY' >/dev/null 2>&1
import sys
raise SystemExit(0 if sys.version_info >= (3, 10) else 1)
PY
then
  echo "The worker requires Python 3.10+ because it uses modern typing syntax and Pydantic v2." >&2
  echo "Selected interpreter: $PYTHON_BIN" >&2
  "$PYTHON_BIN" --version >&2 || true
  exit 1
fi

if [[ -x "$VENV_DIR/bin/python" ]]; then
  if ! "$VENV_DIR/bin/python" - <<'PY' >/dev/null 2>&1
import sys
raise SystemExit(0 if sys.version_info >= (3, 10) else 1)
PY
  then
    rm -rf "$VENV_DIR"
  fi
fi

if [[ ! -d "$VENV_DIR" ]]; then
  "$PYTHON_BIN" -m venv "$VENV_DIR"
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
