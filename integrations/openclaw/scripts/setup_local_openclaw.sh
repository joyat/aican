#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
STATE_DIR="${OPENCLAW_STATE_DIR:-$ROOT_DIR/.openclaw}"
WORKSPACE_DIR="$STATE_DIR/workspace-aican"
TOOLS_DIR="$ROOT_DIR/integrations/openclaw/tools-mcp"

mkdir -p "$STATE_DIR" "$WORKSPACE_DIR"

if ! command -v openclaw >/dev/null 2>&1; then
  echo "openclaw is not installed on PATH" >&2
  exit 1
fi

if ! command -v npm >/dev/null 2>&1; then
  echo "npm is required to install the AiCan MCP tool server dependencies" >&2
  exit 1
fi

export OPENCLAW_STATE_DIR="$STATE_DIR"

openclaw setup --workspace "$WORKSPACE_DIR"
openclaw agents add aican --workspace "$WORKSPACE_DIR" --non-interactive || true
openclaw agents set-identity --agent aican --workspace "$WORKSPACE_DIR" --from-identity || true

cp "$ROOT_DIR"/integrations/openclaw/workspace/AGENTS.md "$WORKSPACE_DIR"/AGENTS.md
cp "$ROOT_DIR"/integrations/openclaw/workspace/SOUL.md "$WORKSPACE_DIR"/SOUL.md
cp "$ROOT_DIR"/integrations/openclaw/workspace/TOOLS.md "$WORKSPACE_DIR"/TOOLS.md
cp "$ROOT_DIR"/integrations/openclaw/workspace/IDENTITY.md "$WORKSPACE_DIR"/IDENTITY.md

(
  cd "$TOOLS_DIR"
  npm install
)

openclaw mcp set aican-tools "$(cat <<JSON
{"command":"node","args":["$TOOLS_DIR/server.mjs"],"cwd":"$TOOLS_DIR","env":{"AICAN_API_BASE_URL":"http://sungas-ubuntulab.tail6932f9.ts.net:5000"}}
JSON
)"

echo "OpenClaw AiCan integration prepared."
echo "State dir: $STATE_DIR"
echo "Workspace: $WORKSPACE_DIR"
