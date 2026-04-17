# AiCan

AiCan is a multi-tenant enterprise workplace assistant. Each client company gets its own named bot with its own personality, guardrails, and document index. Each employee within that company gets a further-personalised instance of that bot.

The runtime architecture is three layers:

- **LM Studio brain** — raw LLM inference on the Mac
- **OpenClaw personality layer** — per-tenant soul files define character, tone, and guardrails
- **AiCan orchestration** — Ubuntu API handles sessions, ACL, conversation history, and RAG retrieval

## Hardware layout

| Machine | Tailscale IP | Role |
|---------|-------------|------|
| Windows desktop | `100.86.148.40` | WPF client — employee-facing |
| Ubuntu server | `100.97.72.86` | ASP.NET Core API (port 5000), AI worker (port 8001), Qdrant (port 6333) |
| Mac | `100.81.186.55` | LM Studio running `google/gemma-4-e4b` on port 1234 |

All three machines communicate over Tailscale VPN.

## Repository layout

```
src/
  AiCan.Contracts/          Shared DTOs and enums (client + server)
  AiCan.Api/                ASP.NET Core API — sessions, chat, intake, retrieval
  AiCan.Desktop/            WPF desktop client

integrations/openclaw/
  workspace/                Global fallback soul files (used when no tenant match)
    SOUL.md
    IDENTITY.md
    AGENTS.md
    tenants/
      sungas.com/           Sungas-specific soul + user registry
        SOUL.md
        IDENTITY.md
        AGENTS.md
        users.json
      laugfsgas.com/        LAUGFS-specific soul + user registry
        SOUL.md
        IDENTITY.md
        AGENTS.md
        users.json

workers/ai_worker/          FastAPI embedding + OCR + extraction worker
deploy/
  ubuntu/                   API deploy and service scripts
  windows/                  Desktop rebuild and relaunch scripts
```

## How a chat message flows

1. Employee types a message in the WPF desktop app.
2. Desktop sends `POST /assistant/chat` to the Ubuntu API (`X-AiCan-Session` header carries the session token).
3. The API's `AssistantOrchestrator` runs:
   - **Session resolve** — the session token maps to a stable `UserId` (Guid). The tenant domain is derived from the user's email.
   - **History** — the last 10 non-system turns are retrieved from `InMemoryConversationStore` (keyed by `UserId`).
   - **Retrieval** — `RetrievalService` embeds an enriched query (current message + tail of last bot reply) and searches Qdrant. The search filter enforces both tenant isolation (`tenant = sungas.com OR common`) and ACL (`access_tags = common | dept:finance | user:<guid>`).
   - **Prompt assembly** — a prompt is built with the bot's name/style, the employee's message, and authorized citation snippets.
   - **LLM call** — `LmStudioProvider` sends a `chat/completions` request to LM Studio. The system message contains the tenant-specific soul files plus the employee profile. The full multi-turn history is prepended so the model has conversational context.
   - **Response** — the LLM reply is returned as `ChatResponse.Message`. Citations appear inline, formatted as `── Sources: title1  ·  title2`.

## Multi-tenant setup

AiCan is multi-tenant from the ground up. Each client company is identified by email domain.

### How tenant resolution works

When an employee connects with `joy@sungas.com`, the API:

1. Extracts the domain: `sungas.com`
2. Looks for `workspace/tenants/sungas.com/users.json` → loads the server-side profile for this email (displayName, botName, department, tone, language). Client-submitted values are overridden by the registry.
3. Loads soul files from `workspace/tenants/sungas.com/SOUL.md`, `IDENTITY.md`, `AGENTS.md`. Falls back to the global `workspace/` soul if no tenant directory is found.
4. Tags all documents uploaded by this user with `tenant: sungas.com` in Qdrant. Retrieval queries are scoped to this tenant automatically.

### Adding a new tenant

1. Create `integrations/openclaw/workspace/tenants/{domain}/`
2. Add `SOUL.md`, `IDENTITY.md`, and `AGENTS.md` (copy from an existing tenant and customise)
3. Add `users.json` with the employee roster (see format below)
4. Copy the three soul files and `users.json` to the same path on Ubuntu under `/home/joyat/projects/aican/integrations/openclaw/workspace/tenants/{domain}/`
5. Restart the API (or wait — soul files are cached per domain at first use, so a restart picks them up cleanly)

No code changes or config changes are needed to add a new tenant.

### Adding or updating users

Edit `tenants/{domain}/users.json`. The API caches the user registry per domain at startup, so a restart is required to pick up new entries from existing deployments.

```json
{
  "joy@sungas.com": {
    "displayName": "Joy S",
    "botName": "SunBot",
    "department": "Finance",
    "role": "User",
    "tone": "WarmProfessional",
    "workStyle": "HelpfulAndConcise",
    "language": "en"
  }
}
```

Valid roles: `User`, `DocAdmin`, `PlatformAdmin`.
Valid tones: any string — injected into the LLM system prompt as-is.
Language codes: `en`, `si` (Sinhala), `ta` (Tamil), or any BCP-47 tag.

Fields not listed fall back to defaults (`WarmProfessional` tone, `HelpfulAndConcise` work style, `en` language).

### Tenant data isolation

Every Qdrant chunk carries a `tenant` payload field. The search filter is a nested Qdrant condition:

```
must:  [ tenant ∈ {sungas.com, common} ]
should: [ access_tags ∈ {common, dept:finance, user:<guid>} ]
```

A Sungas Finance employee will never see a LAUGFS document in their retrieval results, and vice versa. Documents ingested via the watched folder are automatically tagged with the uploader's tenant domain. Seed documents (from `appsettings.json`) receive `tenant: common` and are visible to all tenants.

## Current tenants

| Domain | Bot name | Departments |
|--------|----------|-------------|
| `sungas.com` | SunBot | Finance, Procurement, Operations, HR, IT, Sales |
| `laugfsgas.com` | LaugBot | Finance, Procurement, Operations, HR, IT, Sales, Legal |

### Demo / internal tenant

| Email | Role |
|-------|------|
| `admin@aican.com` | PlatformAdmin |
| `docadmin@aican.com` | DocAdmin |
| `user@aican.com` | User (default demo account) |

The desktop client defaults to `user@aican.com` / `Jo S` / `JoBot` / `IT`. This account has no tenant soul files, so it uses the global fallback soul.

## OpenClaw personality layer

Soul files under `integrations/openclaw/workspace/tenants/{domain}/` define the bot's personality and are loaded by `TenantRegistry.LoadSoul(domain)`:

- `SOUL.md` — tone, guardrails, and what the bot is for
- `IDENTITY.md` — bot name, company, industry context
- `AGENTS.md` — operational rules: department routing, escalation paths, language defaults

These are injected as the LLM **system message** on every request, prefixed to the per-employee profile (name, department, tone, work style, language). The OpenClaw **CLI runner** is disabled (`OpenClaw:Enabled: false`) — the soul files are used as pure prompt content only.

Soul content is cached per domain in memory after the first load. To force a reload without restarting, remove the cached entry by restarting the API process.

## RAG pipeline

Document intake runs centrally on Ubuntu:

1. Desktop uploads or registers a file via `POST /documents/intake/register`.
2. API classifies and stores it under a governed path in `.runtime/repository/`.
3. API chunks extracted text into overlapping passage windows.
4. AI worker embeds each chunk with `intfloat/multilingual-e5-base`.
5. Chunk vectors and payload metadata — including `tenant` and `access_tags` — are written to Qdrant.
6. At query time, retrieval is scoped to the employee's tenant domain and ACL tags.

Qdrant data lives at `{project root}/.data/qdrant` on Ubuntu.

## Configuration

`src/AiCan.Api/appsettings.json` is the committed base config. Ubuntu also has `src/AiCan.Api/appsettings.Local.json` (not committed):

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

`WorkspaceRoot` is the root of the soul file tree. `TenantRegistry` resolves per-tenant files as `{WorkspaceRoot}/tenants/{domain}/`.

## HTTP timeouts

| Layer | Client | Timeout |
|-------|--------|---------|
| Server → LM Studio | `LmStudioProvider` named client | 120 s |
| Server → AI worker | `WorkerEmbeddingProvider` named client | 120 s |
| Desktop → Ubuntu API | `DesktopApiClient` | 180 s |

## Health and status endpoints

- `GET /healthz` — API liveness
- `GET /system/status` — aggregated checks for API, LLM, Worker, and Qdrant; drives the Windows service deck tiles

## Bring-up steps

### Ubuntu

```bash
cd /home/joyat/projects/aican
docker compose up -d qdrant
bash deploy/ubuntu/start_worker.sh
dotnet build src/AiCan.Api/AiCan.Api.csproj -c Release
nohup dotnet src/AiCan.Api/bin/Release/net8.0/AiCan.Api.dll \
  --urls http://0.0.0.0:5000 > .runtime/logs/api.log 2>&1 &
```

Ensure `appsettings.Local.json` exists before starting. The API bootstraps the document catalog into Qdrant on startup.

For an in-place rebuild:

```bash
bash deploy/ubuntu/deploy_api.sh
```

### Windows desktop

```powershell
cd C:\Users\joyat\projects\aican
dotnet build src\AiCan.Desktop\AiCan.Desktop.csproj -c Release
```

For rebuild + interactive relaunch via scheduled task:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\windows\rebuild_desktop.ps1
```

The desktop shortcut at `C:\Users\joyat\Desktop\AiCan.lnk` points to the Release binary.

### Mac

Load `google/gemma-4-e4b` in LM Studio and start the local server on port 1234. No further configuration needed.

## Demo flow

1. Confirm LM Studio is running with the model loaded (Mac).
2. Confirm the API is running: `curl http://100.97.72.86:5000/healthz` (Ubuntu).
3. Open the Windows desktop client and click **Connect Bot**.
4. Try: `When did we last purchase a printer?`
5. Try: `Compose an email to Bright Stationers requesting a quote for 4 printers.`
6. Drop a `.txt` or `.md` file into the watched folder and ask the bot about it.

To demo multi-tenant isolation, connect once as `joy@sungas.com` and once as `kasun@laugfsgas.com`. Each session gets a different bot name, different soul files, and different document retrieval scope.

## Project isolation

AiCan runs on Ubuntu alongside the separate `tinman`/`nemoclaw` project (ports `18789`/`18791`). Do not change AiCan's port (5000) and do not restart tinman services.

## Known limitations

- Email ingestion is not implemented.
- Microsoft 365 OAuth is scaffolded but requires a real Entra app registration.
- The Python worker is synchronous over HTTP; not yet connected to an async job queue.
- Scanned image-only PDFs need OCR hardening for reliable extraction.
- Cold-start latency exists on the first LM Studio or embedding call after model load; run a warm-up query before a live demo.
- Soul file and user registry changes require an API restart to take effect (caches are populated at startup).
