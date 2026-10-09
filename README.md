# StockSync AI

StockSync AI is a local Windows desktop foundation for pharmacy inventory workflows. Phase 2 adds optional, read-only access to documented product, vendor, and category fields in the existing SQL Server `pos` database.

Invoice extraction, matching, verification, POS writes, and undo/redo are deliberately outside this phase.

## Architecture

```text
┌─────────────────────────────────────────────────────────────┐
│ Electron main process                                      │
│ lifecycle · backend supervision · per-launch secret · IPC   │
└──────────────┬───────────────────────────────┬──────────────┘
               │ narrow preload API            │ authenticated HTTP
               │ (no Node.js access)            │ 127.0.0.1:<ephemeral>
┌──────────────▼──────────────┐   ┌────────────▼──────────────┐
│ React + Vite renderer       │   │ ASP.NET Core .NET 8 API  │
│ status shell only           │   │ business-logic boundary  │
└─────────────────────────────┘   └────────────┬──────────────┘
                                               │ migrations
                                  ┌────────────▼──────────────┐
                                  │ StockSync-owned SQLite   │
                                  │ per-user application data│
                                  └───────────────────────────┘

                Optional read-only SQL Server `pos` integration
```

Electron chooses an available loopback port, generates a cryptographically random secret for every launch, starts the backend, and waits up to 15 seconds for an authenticated health response. React cannot access the filesystem, Node.js, SQLite, the API secret, or the API directly. It makes parameterless requests through the preload bridge; Electron validates the IPC sender and performs the authenticated request.

The backend binds with `ListenLocalhost`, applies its SQLite migration transactionally, and fails startup when its launch secret is absent. CORS is intentionally not enabled because no browser origin calls the API. Electron reports startup and unexpected-exit errors and requests graceful backend shutdown when the app exits.

More detail is in [docs/architecture.md](docs/architecture.md).

## Prerequisites

- Windows 10 or later
- Node.js 20 or later and npm 10 or later
- .NET 8 SDK or a later SDK capable of targeting `net8.0`
- Git

This repository's `global.json` prefers .NET 8 and permits a later installed SDK. The current test machine used the .NET 10 SDK to target the installed .NET 8 runtime.

## Initial setup

```powershell
git clone <repository-url>
cd "StockSync AI"
npm install
dotnet restore backend/StockSyncAI.slnx
```

No `.env` file is required for normal Electron development. Copy [.env.example](.env.example) only when testing configuration manually, and never commit secrets.

## Development commands

### React in a browser

```powershell
npm run dev
```

Vite listens only on `127.0.0.1:5173`. A normal browser does not have the Electron preload bridge, so the intentional error state explains that the desktop connection is unavailable.

### Backend by itself

The backend requires a launch secret with at least 32 characters. PowerShell example:

```powershell
$env:STOCKSYNC_API_SECRET = "replace-with-a-long-development-only-value"
$env:STOCKSYNC_BACKEND_PORT = "5187"
$env:STOCKSYNC_DATA_DIR = "$env:LOCALAPPDATA\StockSyncAI"
dotnet run --project backend/StockSyncAI.Api --no-launch-profile
```

Send the secret in the `X-StockSync-Token` header when calling local API routes. Normal users do not start the backend manually; Electron supplies the launch settings and supervises the process.

### Electron development launch

```powershell
npm run electron:dev
```

This command builds the backend, starts Vite, waits for Vite readiness, and launches Electron. Electron itself starts and waits for the backend. Closing the desktop window shuts down the child backend.

Backend source changes are not watched. Restart `npm run electron:dev` after changing C# code.

## Live production-build synchronization

The requested three-terminal workflow is:

```powershell
# One-time initial output (required before sync can watch)
npm run build --workspace frontend
```

Then keep these commands running:

```powershell
# Terminal 1: rebuild frontend/dist on relevant source changes
npm run build:watch

# Terminal 2: atomically synchronize completed builds
npm run sync

# Terminal 3: regular interactive development launch
npm run electron:dev
```

`sync` has exactly one source (`frontend/dist`) and one generated destination (`desktop/renderer`). It first copies into a staging directory, validates `index.html`, then swaps the dedicated destination. A failed copy leaves the previous usable renderer intact. Repeated synchronization is idempotent, and missing build output produces a clear error without changing the destination. `desktop/renderer` is generated and ignored by Git; Electron loads it in production.

Both watchers are designed to stop with Ctrl+C. The sync watcher closes its filesystem watcher on SIGINT/SIGTERM; Vite owns the build watch lifecycle.

For a one-time full build:

```powershell
npm run build
```

That builds React, synchronizes the renderer, and builds the backend in Release configuration.

## Testing and linting

```powershell
npm run lint
npm test
```

`npm test` runs Node tests for port allocation and safe synchronization, Vitest frontend service tests, and xUnit backend tests for SQLite migration and authenticated health behavior.

Useful focused commands:

```powershell
npm run test:node
npm run test --workspace frontend -- --run
dotnet test backend/StockSyncAI.slnx
```

See [docs/phase-1-verification.md](docs/phase-1-verification.md) for the commands and outcomes actually observed in this environment.

## Local data and configuration

Electron stores application-owned data beneath its per-user `userData` directory:

- `data/stocksync.db` — SQLite database
- `data/logs/stocksync-YYYYMMDD.log` — diagnostic logs

Standalone backend runs default to `%LOCALAPPDATA%\StockSyncAI` unless `STOCKSYNC_DATA_DIR` is supplied. The initial migration creates only `__schema_migrations` and `app_metadata`; future domain tables must be added by explicit versioned SQLite migrations.

Configuration variables:

| Variable | Purpose | Current behavior |
| --- | --- | --- |
| `STOCKSYNC_API_SECRET` | Per-launch local API credential | Required; Electron generates it |
| `STOCKSYNC_BACKEND_PORT` | Loopback listener port | Electron supplies an available ephemeral port |
| `STOCKSYNC_DATA_DIR` | Persistent SQLite and log directory | Electron supplies a per-user path |
| `STOCKSYNC_POS_CONNECTION_STRING` | Read-only SQL Server `pos` connection | Optional; absence is reported without blocking startup |

Secrets are never logged. Do not put credentials in source control, `appsettings.json`, or a committed `.env` file.

Configure POS access only with a dedicated SQL Server login that has SELECT permission on the required existing tables and no INSERT, UPDATE, DELETE, DDL, or administrative permissions. The server is not assumed to be local. Example shape:

```powershell
$env:STOCKSYNC_POS_CONNECTION_STRING = "Server=YOUR_SERVER;Database=pos;User ID=stocksync_reader;Password=...;Encrypt=True;TrustServerCertificate=False;Application Intent=ReadOnly"
```

The backend requires `Database=pos` and enforces `Application Intent=ReadOnly`. It exposes authenticated `GET` routes at `/api/pos/status`, `/api/pos/products`, `/api/pos/vendors`, and `/api/pos/categories`. Status responses never contain the connection string. Missing configuration leaves `/health` operational, reports `notConfigured`, and returns HTTP 503 from the three data routes.

## Existing POS database boundary

The SQL Server integration is read-only. It uses explicit column lists through `Microsoft.Data.SqlClient`, performs no writes, and runs no POS migrations. Phase 2 was tested with doubles and deliberately did not connect to a real POS database.

Future work must preserve these supplied names exactly:

- `dbo.stockin`: `vname`, `proname`, `proquantity`, `procost`, `proprice`, `proexpiry`, `pro_barcode`
- `dbo.addpro`: `proname`, `pro_cost`, `proprice`, `procat`, `pro_ID`, `pro_barcode`, `pro_discount`
- read-only `dbo.vendor`: `vname`
- read-only `dbo.addcat`: `catname`

Never add `Batch_ID` or otherwise change the POS schema. Batch IDs, history, mappings, and undo/redo metadata belong in StockSync AI's SQLite database. A future implementation must verify a reliable POS record identifier before attempting targeted undo.

## Windows packaging

```powershell
npm run package:win
```

The command performs the full build, publishes a self-contained `win-x64` backend, and uses electron-builder to create an NSIS installer under `release/`. Production files load from `desktop/renderer`, and the backend executable is packaged as an extra resource outside the Electron ASAR archive.

Build outputs, installer artifacts, local databases, logs, and secrets are ignored by Git.

## Current limitations

- The screen is intentionally limited to application, backend, and POS connection status.
- No production POS credentials were provided, so no real POS connection was attempted or claimed.
- No invoice upload, extraction, templates, fuzzy matching, aliases, pack calculations, verification grid, POS creation/write, batch undo/redo, dashboard, or history UI exists.
- The future Gemini Vision invoice extraction requirement needs internet access. It is not integrated in Phase 1, and the future application must not describe that integration as offline.
- The desktop application and .NET backend themselves are local and require no cloud hosting.
