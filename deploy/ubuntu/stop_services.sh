#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PID_DIR="$ROOT_DIR/.runtime/pids"

stop_pid() {
  local name="$1"
  local pid_file="$2"

  if [[ ! -f "$pid_file" ]]; then
    echo "$name is not running"
    return
  fi

  local pid
  pid="$(cat "$pid_file")"

  if kill -0 "$pid" 2>/dev/null; then
    kill "$pid"
    echo "Stopped $name ($pid)"
  else
    echo "$name pid file exists but process is gone ($pid)"
  fi

  rm -f "$pid_file"
}

stop_pid "API" "$PID_DIR/api.pid"
stop_pid "worker" "$PID_DIR/worker.pid"
