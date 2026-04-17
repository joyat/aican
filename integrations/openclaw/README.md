# OpenClaw Integration

This directory holds the OpenClaw workspace files that give the AiCan assistant its personality. The files are loaded as the LLM system prompt — they are **not** used to run the OpenClaw CLI.

## How it works

`LmStudioProvider` in `src/AiCan.Api/Services.cs` reads the three workspace files once at startup (cached in memory) and injects them as the system message on every LM Studio request, combined with the employee's profile:

```
[soul content from SOUL.md + IDENTITY.md + AGENTS.md]

You are currently acting as {BotName}, the personal assistant for {DisplayName} ({Email}).
Department: {Department}. Role: {Role}.
Preferred language: {PreferredLanguage}. Work style: {WorkStyle}. Tone: {Tone}.
Always stay within the authorized context the user has been given.
Never claim access to documents that were not provided in the authorized context below.
When the employee uploads files, treat them as available only when they appear in the authorized context.
```

The user turn then carries the actual task prompt plus the citation snippets from retrieval.

## Workspace files

| File | Purpose |
|------|---------|
| `SOUL.md` | Tone guardrails — friendly but not casual, never bluff, respect privacy |
| `IDENTITY.md` | Bot name and theme |
| `AGENTS.md` | Operational rules — stay grounded in authorized context, suggest reclassification rather than inventing locations |
| `TOOLS.md` | Tool/action catalogue (referenced by AGENTS.md for future MCP wiring) |

## Configuration

The API reads the workspace directory from `AiCan:WorkspaceRoot` in `appsettings.Local.json`:

```json
{
  "AiCan": {
    "WorkspaceRoot": "/home/joyat/projects/aican/integrations/openclaw/workspace"
  }
}
```

If `WorkspaceRoot` is empty or the files are missing, `LmStudioProvider` falls back gracefully (soul content is omitted; the employee profile section is still injected).

## OpenClaw CLI runner (disabled)

The `AiCan:OpenClaw:Enabled` flag controls whether the API tries to shell out to the `openclaw` CLI binary. This flag is set to `false` in `appsettings.Local.json` because the `openclaw` binary is not installed in the AiCan environment — only the soul/workspace files are used.

The separate `tinman`/`nemoclaw` project on the same Ubuntu server runs a real openclaw-gateway on ports `18789`/`18791`. AiCan does not interact with that project.

## Extending the personality

To adjust the bot's behaviour, edit the workspace files and restart the API (or call the soul reload endpoint if one is added). Changes take effect immediately on the next startup because the files are loaded once at startup.

To give different employees different personalities, the current approach is to maintain one shared workspace. Per-employee soul overrides are a planned future feature.

## Future: MCP tool wiring

`TOOLS.md` and `tools-mcp/` sketch the planned MCP bridge that would let the LLM call AiCan actions (document search, access requests, reclassification) as structured tool calls. This is not yet active — the current retrieval is done server-side by `RetrievalService` before the LLM call, and the results are injected as plain text context.
