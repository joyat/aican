# OpenClaw Integration

This directory turns OpenClaw into the employee-facing assistant runtime while keeping AiCan as the secure business/API layer.

## Integration model

- OpenClaw handles assistant persona, memory, and conversation runtime.
- AiCan remains the system of record for:
  - user/session mapping
  - document search and filing
  - access requests
  - reclassification suggestions
  - audit
- OpenClaw reaches AiCan operations through an MCP tool server defined in `tools-mcp/`.

## Layout

- `workspace/`: OpenClaw workspace files for the `aican` agent
- `tools-mcp/`: MCP server exposing AiCan business actions as typed tools
- `scripts/setup_local_openclaw.sh`: bootstrap helper for Linux hosts

## Expected server layout

Recommended Ubuntu project-local state:

- project root: `/home/joyat/projects/aican`
- OpenClaw state dir: `/home/joyat/projects/aican/.openclaw`
- OpenClaw workspace: `/home/joyat/projects/aican/.openclaw/workspace-aican`

## Bootstrap flow

1. Install OpenClaw and Node/npm on the Ubuntu server.
2. Run `scripts/setup_local_openclaw.sh`.
3. Configure the AiCan API with:
   - `AiCan:OpenClaw:Enabled=true`
   - correct `Command`, `WorkingDirectory`, and `StateDir`
4. Start the AiCan API and ensure OpenClaw can run:

   ```bash
   openclaw agent --agent aican --message "health check" --local
   ```

## Why MCP here

OpenClaw documents an outbound MCP registry under `mcp.servers`, which can be managed with:

- `openclaw mcp list`
- `openclaw mcp set <name> <json>`

This integration uses that registry to define a local stdio MCP server for AiCan tools, so OpenClaw can call:

- document search
- access request
- reclassification suggestion
- profile lookup

## Current limitations

- The repo now includes the MCP server and workspace, but the Ubuntu server still needs the final OpenClaw install and config wiring.
- The current AiCan API runtime shells out to `openclaw agent` and falls back to the built-in orchestrator if OpenClaw is unavailable.
