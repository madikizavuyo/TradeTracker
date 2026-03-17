# TradeTracker Development Rules

## Database Constraints (SmarterASP.NET Compatibility)

- **Strict Key Length:** All Identity-related string keys (Id, UserId, RoleId) must be limited to a maximum length of **128 characters**.
- **Rationale:** The production SQL Server (SmarterASP.NET) has a 900-byte index limit. Default nvarchar(450) keys will cause deployment failure on composite indexes.
- **Implementation:**
  - Always use `.HasMaxLength(128)` in `OnModelCreating` for any Identity or Foreign Key string columns.
  - Do not generate migrations that attempt to revert these lengths to 450.

## Architecture

- **Monorepo Structure:** Backend is in `/TradeHelperAPI` (.NET 9), Frontend is in `/TradeTrackerFrontEnd` (React + Vite).
- **Environment:** Production uses an "All-in-One" setup where the .NET API serves the React static files from `wwwroot`.

## Background Services

- `TrailBlazerBackgroundService` must remain optimized for a shared hosting environment (avoiding excessive memory/CPU spikes).
