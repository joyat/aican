#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

cd "$ROOT_DIR"

if ! command -v openclaw >/dev/null 2>&1; then
  echo "openclaw is required on PATH before bootstrapping" >&2
  exit 1
fi

OPENCLAW_STATE_DIR="${OPENCLAW_STATE_DIR:-$ROOT_DIR/.openclaw}" \
  bash "$ROOT_DIR/integrations/openclaw/scripts/setup_local_openclaw.sh"
