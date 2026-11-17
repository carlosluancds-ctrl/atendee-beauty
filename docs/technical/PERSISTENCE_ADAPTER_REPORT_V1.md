# Persistence adapter report — v1

Prepared: 2026-11-17

## Implemented

- EF Core 10.0.15 and Npgsql EF provider 10.0.3, with Relational explicitly pinned to 10.0.15.
- One immutable tenant-bound scoped `TenantDbContext`; DbContext pooling is disabled.
- EF query filter and SaveChanges tenant ownership validation for the sample tenant table.
- `NpgsqlTenantTransactionRunner`: opens a closed connection, resets session role and any prior session-level tenant GUC on checkout, begins a transaction, executes fixed `SET LOCAL ROLE atendee_runtime`, sets tenant context with a bound parameter and `set_config(..., true)`, executes the operation, saves, commits and closes the connection.
- Sample repository derives tenant from trusted server context; the caller cannot provide tenant_id.
- Separate owner, runtime, app-login and migrator role template, plus RLS migration for sample table.
- Optional integration runner verifies same backend reuse (pool size 1), cross-tenant reads, cross-tenant insert rejection, no direct table privileges for app-login and rollback recovery.

## Build and dependency verification

- Full six-project solution build in Release: success, 0 warnings, 0 errors.
- Restore in `--locked-mode`: success.
- Domain checks: 15/15 passed.
- Persistence integration runner compiles and safely skips when no test connection string is configured.
- NuGet audit on 2026-11-17 reported no vulnerable packages in the infrastructure and persistence-check project graphs.

## Not yet verified

- No PostgreSQL 18.7 server test was run. RLS policy behavior, pool reuse and database grants remain unverified at runtime.
- The current Windows Server has Docker in Windows-container mode; no service was started, no PostgreSQL 18 was installed and PostgreSQL 14 remains stopped/disabled.
- No production authentication / immutable `ICurrentTenantContext` implementation exists yet.
- `tenant_notes` is only a sample vertical slice; customer, service, professional, appointment, conversation and billing tables still require design, migrations, RLS and integration tests.

Do not deploy this adapter or use it with real data until the PostgreSQL integration suite and authentication/authorization have been implemented and passed.
