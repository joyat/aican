# AiCan V1

AiCan is a Windows desktop bot backed by a centralized server for authentication, assistant memory, secured retrieval, audit, and document intake.

## Repository layout

- `src/AiCan.Contracts`: shared DTOs and enums used by desktop and server
- `src/AiCan.Api`: ASP.NET Core API for sessions, profiles, chat, intake, and actions
- `src/AiCan.Desktop`: WPF desktop client with onboarding, Microsoft 365 sign-in scaffolding, chat, and watched-folder helper
- `workers/ai_worker`: FastAPI worker skeleton for OCR/extraction/classification/embedding
- `integrations/openclaw`: OpenClaw workspace, setup script, and MCP tool server for employee-facing bot runtime
- `docker-compose.yml`: local PostgreSQL and Qdrant services

## Current implementation scope

This scaffold implements the core v1 shape:

- single-user bot profile with friendly preferences and private conversation history
- central session exchange and role mapping
- assistant chat endpoint with citations and low-risk suggested actions
- document intake registration with governed repository path generation
- watched-folder helper on the desktop client
- provider interfaces for LLM, embeddings, OCR, parsing, classification, and vector storage
- OpenClaw runtime routing support with fallback to the built-in assistant
- document search endpoints and OpenClaw MCP tool exposure scaffolding

The code intentionally uses in-memory stores for application state while keeping the boundaries needed to replace them with PostgreSQL, Qdrant, and persistent background jobs.

## Server configuration

`src/AiCan.Api/appsettings.json` contains:

- repository root
- LM Studio endpoint
- seeded users and roles

The API is written so the `ILLMProvider`, `IEmbeddingProvider`, and `IVectorStore` implementations can be swapped without changing the HTTP surface.

## OpenClaw mode

The API can use OpenClaw as the employee-facing assistant runtime.

- set `AiCan:OpenClaw:Enabled=true`
- ensure the `openclaw` CLI is installed on the server
- prepare the OpenClaw workspace and MCP bridge with:

  ```bash
  integrations/openclaw/scripts/setup_local_openclaw.sh
  ```

When OpenClaw mode is enabled, `/assistant/chat` shells out to `openclaw agent` and falls back to the built-in orchestrator if OpenClaw is unavailable.

## Project isolation

The server runtime is intended to stay self-contained inside the project folder.

- Docker services use bind mounts under `./.data/`
- application code stays under `src/` and `workers/`
- project docs and compose files stay at the repo root

This avoids mixing AiCan runtime data with other projects on the same server.

## Desktop client notes

The WPF desktop client targets Windows and is designed to:

- sign the user in through Microsoft 365 using MSAL
- let the user name their bot and store preferences locally
- keep a watched folder active in the background
- call the central server for profile, chat, and intake flows

## Worker notes

The Python worker is a thin service boundary for:

- text extraction
- OCR placeholder orchestration
- heuristics-based classification
- deterministic embedding placeholder output

## Bring-up steps

1. Start PostgreSQL and Qdrant:

   ```bash
   docker compose up -d
   ```

2. Build and run the API on a machine with the .NET SDK installed.
3. Point `AiCan.Api` at your LM Studio host.
4. Build and install the Windows desktop app on the client machine.
5. Run the Python worker where document enrichment jobs should execute.

## Prototype limitations

- persistence is in-memory inside the API process
- email ingestion is not implemented yet
- OAuth app registration values must be supplied for real Microsoft 365 sign-in
- the worker exposes extraction/classification primitives but is not yet wired to an async job queue
