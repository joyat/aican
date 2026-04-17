# AiCan V1

AiCan is a Windows desktop bot backed by a centralized Ubuntu API server. The runtime structure is:

- LM Studio brain
- OpenClaw soul/personality
- AiCan secured comms + ACL + chunked RAG

Each employee gets a named personal assistant (e.g. "JoBot" for Jo S). The LLM brain runs in LM Studio on the Mac. The secured retrieval layer, chunk index, and ACL enforcement run centrally on Ubuntu. All three machines connect over Tailscale.

## Hardware layout

| Machine | Tailscale IP | Role |
|---------|-------------|------|
| Windows desktop | `100.86.148.40` | WPF client — employee-facing |
| Ubuntu server | `100.97.72.86` | ASP.NET Core API on port 5000, AI worker on port 8001, Qdrant on port 6333 |
| Mac | `100.81.186.55` | LM Studio running `google/gemma-4-e4b` on port 1234 |

## Repository layout

- `src/AiCan.Contracts` — shared DTOs and enums used by both client and server
- `src/AiCan.Api` — ASP.NET Core API handling sessions, profiles, chat, document intake, and retrieval
- `src/AiCan.Desktop` — WPF desktop client with onboarding, chat UI, and watched-folder helper
- `integrations/openclaw/workspace/` — soul/identity files (`SOUL.md`, `IDENTITY.md`, `AGENTS.md`) that define the bot's personality
- `workers/ai_worker` — FastAPI worker skeleton for OCR, extraction, classification, and embedding (not yet wired to async queue)

## How a chat message flows

1. Employee types a message in the WPF desktop app.
2. Desktop sends `POST /assistant/chat` to the Ubuntu API (with the session token in `X-AiCan-Session`).
3. The API's `AssistantOrchestrator` runs:
   a. **Instant path** — if the message is `hi`, `hello`, `hey`, or `who are you`, a canned reply is returned immediately (no LLM call).
   b. **Retrieval** — `RetrievalService` embeds the query, searches chunk vectors in Qdrant with access-tag filters, and returns authorized `CitationDto` objects.
   c. **Prompt assembly** — a user-turn prompt is built containing the bot's name/style, the employee's message, and the authorized citation snippets.
   d. **LLM call** — `LmStudioProvider` sends a `chat/completions` request to LM Studio on the Mac. The system message contains the OpenClaw soul files (SOUL.md + IDENTITY.md + AGENTS.md) plus the employee profile (name, department, tone, work style, language).
   e. **Response** — the LLM's reply is returned as `ChatResponse.Message`. Citations appear inline at the bottom of the same bubble, formatted as `── Sources: title1  ·  title2`.

## RAG pipeline

Document intake is centralized on Ubuntu:

1. The desktop client uploads or registers a file.
2. The API classifies and stores it under a governed repository path in `.runtime/repository/`.
3. The API chunks extracted text into overlapping passage windows.
4. The AI worker embeds each chunk with `intfloat/multilingual-e5-base`.
5. Chunk vectors and payload metadata are written into Qdrant.
6. At query time, AiCan searches only chunks the employee is allowed to access.

Qdrant is kept on the Ubuntu server under `./.data/qdrant`, not on user PCs.

## OpenClaw soul files

The files under `integrations/openclaw/workspace/` define the bot's personality and are loaded once at startup by `LmStudioProvider.LoadSoulContent()`:

- `SOUL.md` — tone and guardrails ("friendly without casual", "never bluff")
- `IDENTITY.md` — name and theme
- `AGENTS.md` — operational rules (stay grounded in authorized context, suggest reclassification when needed)

These files are injected as the LLM **system message** on every request, combined with the employee's profile. The OpenClaw CLI runner is **disabled** (`OpenClaw:Enabled: false` in `appsettings.Local.json`) — the soul files are used as pure prompt content, not to launch an external process.

To give a different employee a different personality, edit or extend the workspace files or add per-employee overrides in the profile.

## Configuration

`src/AiCan.Api/appsettings.json` is the base config committed to the repo. The Ubuntu server also has `src/AiCan.Api/appsettings.Local.json` (not committed) which overrides the relevant values:

```json
{
  "AiCan": {
    "LmStudioBaseUrl": "http://100.81.186.55:1234/v1",
    "WorkspaceRoot": "/home/joyat/projects/aican/integrations/openclaw/workspace",
    "OpenClaw": {
      "Enabled": false,
      "WorkingDirectory": "/home/joyat/projects/aican",
      "StateDir": "/home/joyat/projects/aican/.openclaw"
    }
  }
}
```

`appsettings.Local.json` is loaded explicitly in `Program.cs` and takes precedence over `appsettings.json`.

## HTTP client timeouts

LM Studio over Tailscale is slow. Two timeouts are set to prevent hung UIs:

- **Server** (`LmStudioProvider` named HTTP client): 120 seconds — configured in `Program.cs` via `AddHttpClient`.
- **Server** (`WorkerEmbeddingProvider` named HTTP client): defaults to 120 seconds for batch embedding calls.
- **Desktop client** (`DesktopApiClient`): 180 seconds — matches the server-side budget with margin.

## Bring-up steps (Ubuntu + Windows)

### Ubuntu infrastructure

```bash
# From the project root on Ubuntu
cd /home/joyat/projects/aican
docker compose up -d qdrant
bash deploy/ubuntu/start_worker.sh
dotnet build src/AiCan.Api/AiCan.Api.csproj -c Release
nohup dotnet src/AiCan.Api/bin/Release/net8.0/AiCan.Api.dll \
  --urls http://0.0.0.0:5000 > .runtime/logs/api.log 2>&1 &
```

Make sure `appsettings.Local.json` exists with the correct `LmStudioBaseUrl` before starting. The API will bootstrap the current catalog into Qdrant on startup.

### Windows desktop

```powershell
# From the project root on Windows
cd C:\Users\joyat\projects\aican
dotnet build src\AiCan.Desktop\AiCan.Desktop.csproj -c Release
```

Launch the built `.exe` from `src\AiCan.Desktop\bin\Release\net8.0-windows10.0.19041.0\`. If launching from an SSH session into an RDP machine, use a scheduled task with the `/it` flag so it launches in the interactive user session.

### Mac (LM Studio)

Load `google/gemma-4-e4b` in LM Studio and start the local server on port 1234. No other configuration needed on the Mac.

## Demo flow

1. On the Mac, confirm LM Studio is running with the model loaded.
2. On Ubuntu, confirm the API is running (`curl http://100.97.72.86:5000/healthz`).
3. Open the Windows desktop client and click **Connect Bot**.
4. Try a starter prompt such as `When did we last purchase a printer?`
5. Ask it to compose an email: `Compose an email to Bright Stationers requesting a quote for 4 printers.`
6. Drop a `.txt` or `.md` file into the watched folder and ask the bot about it.

## Demo tenant

The default seeded tenant uses `aican.com`.

| Email | Role |
|-------|------|
| `admin@aican.com` | PlatformAdmin |
| `docadmin@aican.com` | DocAdmin |
| `user@aican.com` | User (default demo) |

The desktop client defaults to `user@aican.com` / `Jo S` / `JoBot` / `Finance` so the first-run demo needs no additional setup.

## Project isolation

AiCan is intentionally isolated from the `tinman`/`nemoclaw` project that also runs on the same Ubuntu server on ports `18789`/`18791`. Do not change AiCan's port (5000) and do not restart or modify tinman services.

## Document intake and retrieval

Files placed in the watched folder (or manually uploaded) are registered via `POST /documents/intake/register`. The API:

1. Assigns a governed repository path based on department, visibility scope, and date.
2. Writes the file to `.runtime/repository/`.
3. Adds the document to `InMemoryDocumentCatalog` (file-backed for persistence across restarts).
4. Chunks the extracted text using overlapping passage windows.
5. Sends passage batches to the AI worker for multilingual embeddings.
6. Stores chunk vectors and ACL payload metadata in Qdrant.
7. On chat queries, retrieves authorized chunks semantically and turns them into citations.

## Current limitations

- Email ingestion is not yet implemented.
- Microsoft 365 OAuth is scaffolded but requires a real Entra app registration to activate.
- The Python worker is still synchronous and fronted by HTTP; it is not yet wired to an async job queue.
- Per-employee soul file overrides are not yet implemented — all employees share the same workspace files.
