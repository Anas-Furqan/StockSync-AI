# Architecture

Phase 1 uses three processes with explicit boundaries:

1. Electron owns desktop lifecycle and supervises the local backend.
2. React renders the minimal application shell in Electron's sandboxed renderer.
3. ASP.NET Core owns configuration, local persistence, and future business logic.

The future SQL Server adapter will preserve the supplied `pos` schema exactly and will never migrate that database. Application-owned metadata belongs in SQLite.
