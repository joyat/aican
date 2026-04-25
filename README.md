# AiCan

AiCan is a multi-tenant workplace assistant platform for internal knowledge retrieval, document intake, and guided chat. It combines a desktop client, a .NET API, a Python AI worker, tenant-scoped prompt configuration, and a vector index so each organization can run the same product with isolated identity, policy, and retrieval context.

The repository is public. Infrastructure endpoints, host-specific paths, and machine-specific credentials are intentionally excluded from the committed defaults. Content under `integrations/openclaw/workspace/tenants/` should be treated as synthetic demo data or public bootstrap material only. Do not commit confidential customer documents or internal-only customer facts.

## What the system does

- Provides a desktop chat experience for employees
- Resolves a user into a tenant and assistant profile
- Stores conversation state and assistant preferences
- Registers and classifies uploaded documents
- Extracts text, chunks content, and embeds passages
- Indexes vectors in Qdrant and retrieves tenant-authorized context
- Injects tenant-specific prompt files as the assistant identity layer

## Architecture

| Layer | Technology | Responsibility |
|------|------------|----------------|
| Desktop client | WPF on .NET 8 | Employee UI, session bootstrap, chat, profile updates, watched-folder intake |
| API | ASP.NET Core Minimal API on .NET 8 | Session exchange, assistant orchestration, tenant resolution, document intake, retrieval, audit events |
| AI worker | FastAPI + Python | Document text extraction, simple classification, embedding generation |
| Vector store | Qdrant | Passage storage and similarity search |
| LLM endpoint | LM Studio or another OpenAI-compatible endpoint | Final response generation |
| Prompt layer | OpenClaw workspace files | Tenant identity, tone, guardrails, routing instructions |

## Request flow

1. The desktop client exchanges a session with the API using an email, display name, department, and bot name.
2. The API resolves the user profile. If a tenant `users.json` entry exists, the server-side tenant profile overrides client-supplied values.
3. Chat requests are routed through the assistant runtime, which loads tenant prompt files and recent conversation history.
4. Retrieval embeds the current query, applies tenant and access filters, and fetches authorized passages from Qdrant.
5. The API assembles the final prompt from the tenant soul, employee profile, recent history, and citations.
6. The LLM endpoint generates the answer, and the API returns the response with citations.

## Multi-tenant model

AiCan treats the email domain as the tenant key. Each tenant can define:

- `SOUL.md`: behavior and tone
- `IDENTITY.md`: company and assistant identity
- `AGENTS.md`: routing and operating rules
- `users.json`: tenant-owned user registry and profile overrides
- `docs/`: synthetic example documents used for retrieval tests and demos

If no tenant directory exists for a user domain, the API falls back to the global workspace files under `integrations/openclaw/workspace/`.

## Repository layout

```text
src/
  AiCan.Api/                 ASP.NET Core API and orchestration layer
  AiCan.Contracts/           Shared DTOs and enums
  AiCan.Desktop/             WPF desktop client

workers/
  ai_worker/                 FastAPI worker for extraction and embeddings

integrations/
  openclaw/
    workspace/               Tenant and global prompt files
    tools-mcp/               Early MCP bridge scaffolding

deploy/
  ubuntu/                    Linux helper scripts
  windows/                   Windows helper scripts
```

## Core implementation details

### API

The API is implemented with ASP.NET Core Minimal APIs. Important responsibilities include:

- session management via `IUserDirectory`
- assistant profile persistence via `IAssistantProfileStore`
- prompt assembly and response generation via `IAssistantRuntime`
- document intake and indexing via `IDocumentIntakeService` and `IDocumentIndexer`
- vector retrieval via `IRetrievalService` and `IVectorStore`

State is currently file-backed under `.runtime/` through simple JSON stores. This keeps the MVP easy to run locally, but it is not intended as a production persistence strategy.

### Worker

The worker exposes:

- `/extract` for file text extraction
- `/classify` for lightweight heuristic categorization
- `/embed` and `/embed-batch` for vector generation

The current implementation uses:

- `pypdf` for PDFs
- `python-docx` for Word documents
- `openpyxl` for spreadsheets
- `sentence-transformers` for embeddings

### Retrieval and authorization

Chunk vectors are written to Qdrant with tenant and document metadata. Retrieval applies both tenant scoping and document visibility rules before passages are included in the final prompt. This keeps the LLM grounded in the authorized slice of the workspace instead of the full corpus.

### Prompt orchestration

The OpenClaw integration is used as a prompt layer, not as an actively invoked CLI runtime in the default configuration. Tenant soul files are loaded from disk and prepended to the model request as system context.

## Configuration

The committed base config in [`src/AiCan.Api/appsettings.json`](src/AiCan.Api/appsettings.json) uses repository-safe defaults. Machine-specific overrides belong in `src/AiCan.Api/appsettings.Local.json`, which is git-ignored.

Example local override:

```json
{
  "AiCan": {
    "LmStudioBaseUrl": "http://127.0.0.1:1234/v1",
    "WorkspaceRoot": "integrations/openclaw/workspace",
    "OpenClaw": {
      "WorkingDirectory": ".",
      "StateDir": ".openclaw"
    }
  }
}
```

Typical settings to override locally:

- the LLM endpoint URL
- the worker base URL if it is not on `127.0.0.1:8001`
- the workspace root if the API is launched outside the repository root
- any OpenClaw CLI-specific paths if that runtime is enabled

## Local development

### Prerequisites

- .NET 8 SDK
- Python 3.10+ environment for the worker
- Docker for Qdrant
- An OpenAI-compatible chat endpoint such as LM Studio

### Start the supporting services

```bash
docker compose up -d qdrant
```

```bash
cd workers/ai_worker
pip install -r requirements.txt
uvicorn main:app --host 127.0.0.1 --port 8001
```

```bash
dotnet run --project src/AiCan.Api/AiCan.Api.csproj
```

The default demo API endpoint used by the desktop client and smoke test is `http://sungas-ubuntulab.tail6932f9.ts.net:5000`.
For local-only development, override the desktop server URL or run `AICAN_BASE_URL=http://127.0.0.1:5000 bash scripts/demo_smoke_test.sh`.

You can also use the helper scripts:

```bash
bash deploy/ubuntu/start_worker.sh
bash deploy/ubuntu/start_api.sh
```

Run the end-to-end smoke test after the API, worker, and Qdrant are up:

```bash
bash scripts/demo_smoke_test.sh
```

The first worker startup may take longer because `sentence-transformers` may need to download and warm the embedding model before `/healthz` reports ready.

Build the desktop client from Windows:

```powershell
dotnet build src\AiCan.Desktop\AiCan.Desktop.csproj -c Release
```

Publish the versioned self-contained desktop package from Windows:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\windows\publish_desktop.ps1
```

This produces a versioned folder under `artifacts\desktop\` and a matching zip on the Windows desktop, for example `AiCan-Desktop-v5.1-win-x64.zip`.

### Microsoft 365 sign-in

The Windows desktop client can use Microsoft 365 interactive sign-in through MSAL. It now reads these environment variables at runtime:

- `AICAN_M365_CLIENT_ID`
- `AICAN_M365_TENANT_ID` (recommended; falls back to `common` if omitted)

Example on Windows PowerShell:

```powershell
$env:AICAN_M365_CLIENT_ID = "<entra-app-client-id>"
$env:AICAN_M365_TENANT_ID = "<entra-tenant-id>"
```

Current scope requested by the desktop client: `User.Read`.

The current code uses Microsoft sign-in to authenticate the desktop user and bootstrap the AiCan session. Fine-grained authorization inside AiCan is still driven by the user email domain plus the tenant `users.json` registry.

## Public repo hygiene

The repository now follows these rules:

- committed config stays generic and machine-agnostic
- host-specific overrides go in ignored local config
- helper scripts resolve paths dynamically instead of embedding usernames
- tenant demo data is explicitly synthetic

## Current limitations

- persistence is JSON-file based rather than database-backed
- classification is heuristic and intentionally simple
- the worker runs synchronously and is not yet designed for high throughput
- desktop defaults assume a local or explicitly configured API endpoint
- OpenClaw CLI integration exists behind a flag but is not the default runtime path

## Related documentation

- [`integrations/openclaw/README.md`](integrations/openclaw/README.md) for the prompt-layer integration details
- [`integrations/openclaw/workspace/README.md`](integrations/openclaw/workspace/README.md) for notes on synthetic tenant data
