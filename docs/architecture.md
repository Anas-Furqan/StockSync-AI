# Architecture decisions

## Process and trust boundaries

Phase 1 uses three processes with explicit responsibilities:

1. Electron owns desktop lifecycle, selects a loopback port, creates the per-launch credential, supervises .NET, and mediates narrow IPC.
2. React renders the minimal shell in an Electron sandbox with context isolation enabled and Node integration disabled.
3. ASP.NET Core owns configuration validation, local API endpoints, SQLite persistence, logging, and all future business logic.

The renderer receives neither the secret nor the backend address. This reduces accidental exposure and prevents normal web content from calling administrative endpoints. Navigation away from the application is blocked; new HTTPS links may open only in the system browser.

## Startup sequence

1. Electron allocates an ephemeral port bound through `ListenLocalhost`.
2. Electron generates a random 256-bit secret and passes it only in the child environment.
3. .NET validates configuration and applies SQLite migrations transactionally.
4. Electron polls authenticated `/health` with a finite timeout.
5. Only after a `ready` response does Electron load the React development URL or packaged assets.
6. React asks for `/api/status` indirectly through validated IPC and displays Connected only after success.

On desktop exit, Electron posts to the authenticated shutdown endpoint, waits up to three seconds, and terminates the child only if graceful shutdown did not complete. Unexpected child exit is retained as an error state and shown to the user.

## Persistence

SQLite is private to StockSync AI and lives with logs in persistent per-user application data. Versioned migrations are recorded in `__schema_migrations`. Phase 1 creates only minimal metadata infrastructure.

The future SQL Server adapter must preserve the supplied `pos` schema exactly and must never run migrations against that database. The current `IPointOfSaleGateway` is intentionally not implemented or registered, making POS access impossible in Phase 1.

## Development and production assets

Development loads `http://127.0.0.1:5173` after the Vite readiness check. Production loads `desktop/renderer/index.html`. The sync script stages and validates Vite output before swapping that dedicated generated directory, so Electron never observes a half-copied build.
