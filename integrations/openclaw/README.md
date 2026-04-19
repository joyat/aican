# OpenClaw Integration

This directory contains the prompt-layer integration for AiCan. The OpenClaw workspace files define tenant identity, tone, and operating rules. In the default setup, AiCan reads these files and injects them into the model request as system context; it does not rely on an actively running OpenClaw CLI process.

## How it is used

`TenantRegistry` loads `SOUL.md`, `IDENTITY.md`, and `AGENTS.md` from:

- the tenant directory at `workspace/tenants/{domain}/`, when present
- the global `workspace/` directory as a fallback

That content is combined with the employee profile and recent conversation history before the request is sent to the configured LLM endpoint.

## Workspace files

| File | Purpose |
|------|---------|
| `SOUL.md` | Tone, guardrails, and response constraints |
| `IDENTITY.md` | Assistant identity and tenant context |
| `AGENTS.md` | Routing rules and operating guidance |
| `TOOLS.md` | Placeholder tool catalog for future MCP-based actions |

## Configuration

The workspace root is configured through `AiCan:WorkspaceRoot`. A repository-safe example is:

```json
{
  "AiCan": {
    "WorkspaceRoot": "integrations/openclaw/workspace"
  }
}
```

If `WorkspaceRoot` is blank or the files are missing, the API falls back to a generic assistant system prompt.

## Synthetic demo data

The sample tenant directories in `workspace/tenants/` are demo fixtures. Domains, employee names, email addresses, policies, and example documents are synthetic and are included only to exercise the multi-tenant prompt and retrieval flow.

## OpenClaw CLI support

The codebase includes an `AiCan:OpenClaw:Enabled` switch and a runner abstraction for CLI-based execution. That path is optional and disabled in the default configuration. The primary runtime path in this repository is direct prompt injection plus server-side retrieval.

## Future direction

`TOOLS.md` and `tools-mcp/` outline a future MCP bridge for structured actions such as document search, access requests, and reclassification. Today, those operations remain server-side and their results are injected into the prompt as plain context.
