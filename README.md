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
- document intake registration with governed repository path generation and binary upload support
- watched-folder helper and manual upload flow on the desktop client
- provider interfaces for LLM, embeddings, OCR, parsing, classification, and vector storage
- OpenClaw runtime routing support with fallback to the built-in assistant
- file-backed persistence for sessions, profiles, conversation history, document catalog state, and audit output
- seeded demo tenant data and seeded demo documents for a live first-run walkthrough
- document search endpoints and OpenClaw MCP tool exposure scaffolding

## Server configuration

`src/AiCan.Api/appsettings.json` contains:

- repository root and state root
- LM Studio endpoint
- seeded users, roles, and demo documents

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

OpenClaw is also wrapped with a server-side wall-clock timeout so the desktop chat experience does not hang indefinitely if the external bot runtime is slow.

## Project isolation

The server runtime is intended to stay self-contained inside the project folder.

- Docker services use bind mounts under `./.data/`
- application code stays under `src/` and `workers/`
- project docs and compose files stay at the repo root

This avoids mixing AiCan runtime data with other projects on the same server.

## Desktop client notes

The WPF desktop client targets Windows and is designed to:

- connect directly to the AiCan server for a demo-safe flow
- optionally attempt Microsoft 365 sign-in once a real Entra app is configured
- let the user name their bot and store preferences locally
- restore recent chat history after reconnect
- keep a watched folder active in the background
- support manual file upload for live demos
- call the central server for profile, chat, history, and intake flows

## Demo tenant

The default seeded tenant uses the hypothetical domain `aican.com`.

- `admin@aican.com`
- `docadmin@aican.com`
- `user@aican.com`

The desktop client defaults to the `user@aican.com` demo profile and a named bot (`RafiBot`) so the first-run demo works without additional setup.

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

## First demo flow

1. Start the Ubuntu helper scripts or equivalent services.
2. Open the Windows desktop client.
3. Confirm the server URL points to the Ubuntu API.
4. Click `Connect Bot`.
5. Use a starter prompt such as `When did we last purchase a printer?`
6. Upload a text file or drop one into the watched folder and ask the bot about it.

## Ubuntu server helper scripts

For the current prototype layout with Ubuntu as the central server:

- `deploy/ubuntu/bootstrap_openclaw.sh`
  - prepares the OpenClaw workspace and the local AiCan MCP bridge
- `deploy/ubuntu/start_worker.sh`
  - creates `.venv/worker`, installs worker dependencies, and starts the worker on port `8001`
- `deploy/ubuntu/start_api.sh`
  - starts the ASP.NET Core API on port `5000`
- `deploy/ubuntu/stop_services.sh`
  - stops the API and worker processes started by the helper scripts

The helper scripts keep runtime state inside the repo:

- logs: `.runtime/logs/`
- pid files: `.runtime/pids/`
- OpenClaw state: `.openclaw/`

For a real deployment on Ubuntu, create an ignored `src/AiCan.Api/appsettings.Local.json` file and set:

- `AiCan:LmStudioBaseUrl` to the Mac host reachable from Ubuntu
- `AiCan:OpenClaw:Enabled` to `true`
- `AiCan:OpenClaw:WorkingDirectory` to `/home/joyat/projects/aican`
- `AiCan:OpenClaw:StateDir` to `/home/joyat/projects/aican/.openclaw`
- optionally override `AiCan:RepositoryRoot` and `AiCan:StateRoot` if you want the stored documents and persisted app state outside the project tree

## Current limitations

- email ingestion is not implemented yet
- OAuth app registration values must be supplied for real Microsoft 365 sign-in
- the worker exposes extraction/classification primitives but is not yet wired to an async job queue
- the current vector store and database services are scaffolded but not yet used as the authoritative persistent source of truth
