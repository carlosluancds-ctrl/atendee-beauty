# Persistence adapter report — v2

Prepared: 2026-11-17

## Current vertical slice

- EF Core 10.0.15 and Npgsql EF Core provider / driver 10.0.3.
- `TenantDbContext` captures one trusted tenant ID when the scoped context is created; EF query filter and SaveChanges ownership checks supplement database RLS.
- Context pooling is intentionally disabled (`AddDbContext`, not `AddDbContextPool`).
- `NpgsqlTenantTransactionRunner` requires a closed connection and no existing transaction at entry; clears stale role and tenant session state; opens a transaction; uses a fixed `SET LOCAL ROLE atendee_runtime`; sets `app.tenant_id` using a bound parameter and transaction-local `set_config`; executes the operation, saves and commits; closes the connection on success or failure.
- `EfTenantNoteStore` derives tenant identity from trusted server-side context. Repository methods do not accept tenant ID from callers.
- DB role template separates owner, runtime, app login and migrator. The API login receives no direct table grants; only the constrained runtime role receives schema/table access, subject to RLS.
- Optional persistence runner tests A/B scopes, known UUID reads, attempted cross-tenant writes, direct table access without runner, physical backend reuse (pool size 1) and rollback behavior.

## Verified in this validation round

- Six-project solution Release build: successful, 0 warnings, 0 errors.
- `dotnet restore --locked-mode`: successful.
- Domain checks: 15/15 passed.
- Persistence integration runner compiles; without a configured disposable DB string it reports SKIP and makes no connection.
- NuGet vulnerability audit for Infrastructure and Persistence.Checks: no known vulnerable packages reported by configured sources on 2026-11-17.

## Mandatory caveats

- PostgreSQL 18.7 runtime and connection-pool tests have NOT run. No RLS or role-grant behavior is certified at runtime.
- The Windows Server Docker Engine is in Windows-container mode; no PostgreSQL 18 service/container was installed or started there. PostgreSQL 14.24 service remains stopped/disabled.
- Authentication and the production `ICurrentTenantContext` remain unimplemented. Persistence registration is not wired into API startup.
- A custom PostgreSQL GUC is not a secret: a session with arbitrary SQL execution can set it. RLS is defense in depth, not an SQL-injection shield. Never expose dynamic SQL or trust tenant values from HTTP inputs.
- `tenant_notes` is sample-only; all actual business tables need individual tenant classification, composite keys/foreign keys, RLS policies and cross-tenant tests.

No production use is permitted until the real PostgreSQL integration suite, identity/authorization and full tenant schema are complete and approved.
