# Tenant database context — v1

## Core rule

A `TenantDbContext` is created only after the authenticated server-side request has resolved one immutable tenant. One scoped DbContext belongs to exactly one tenant. Do not register it with `AddDbContextPool`; do not reuse it across tenants or queue messages.

## Mandatory request flow

1. Authentication middleware validates the identity.
2. Server-side identity resolution loads the operational user and its one tenant membership.
3. Authorization verifies the user's role and resource scope.
4. The scoped `ICurrentTenantContext` exposes the trusted tenant ID. It never reads a tenant ID directly from route, query, header or request body.
5. `ITenantTransactionRunner` opens a physical Npgsql connection and starts one transaction.
6. Before the transaction, the runner resets session role and clears any prior session-level `app.tenant_id` setting on that checked-out connection.
7. Inside the transaction, the runner executes the fixed SQL `SET LOCAL ROLE atendee_runtime` and parameterized `set_config('app.tenant_id', @tenant_id, true)`.
8. The application operation runs; `SaveChanges` and commit occur within the same transaction.
9. Commit/rollback resets the transaction-local role and tenant setting. Connection is closed and returned to Npgsql pool.

All tenant-scoped reads and writes must run through this boundary. A query outside it runs as `atendee_app_login`, which has no direct table privileges, and must fail closed.

## Database roles

- `atendee_owner`: NOLOGIN; owns schema/tables; used only by migrations after `SET ROLE`.
- `atendee_runtime`: NOLOGIN, NOINHERIT, NOSUPERUSER, NOBYPASSRLS; receives only required table privileges and is the role targeted by RLS policies.
- `atendee_app_login`: LOGIN, NOINHERIT, NOSUPERUSER, NOBYPASSRLS; has membership with SET (not INHERIT) in `atendee_runtime`, but no direct table grants.
- `atendee_migrator`: separate LOGIN for deployment migrations; may SET ROLE to `atendee_owner`; never used by API or worker traffic.

Provision both LOGIN passwords outside SQL files and source control, using a secret manager and approved authentication method. The template intentionally contains no password.

## EF Core protections

- `TenantDbContext` captures tenant ID once at construction; scope identity cannot be switched afterwards.
- A global query filter provides an additional guard for the sample tenant table.
- `SaveChanges` checks that added/modified/deleted rows belong to the context tenant.
- Database RLS remains authoritative; application filters do not replace it.
- Use `AddDbContext`, not `AddDbContextPool`, until a separate design and tests prove safe pooling.

## Failure behavior

- Missing/invalid tenant: context construction or transaction start fails.
- DbContext connection is already open or a transaction already exists: runner rejects the operation rather than guessing ownership.
- Invalid runtime role membership or missing DB grants: operation fails; no fallback to an elevated DB account.
- SQL injection remains a serious threat: a database user able to execute arbitrary SQL can potentially change custom GUCs. Keep parameterized SQL, prohibit dynamic SQL from user input, minimize DB grants and test the application boundary. RLS is defense in depth, not an SQL-injection shield.

## Current scope

The `tenant_notes` table is a small vertical slice to validate wiring. It is not a customer, appointment, billing or production data model. More domain tables must be added with explicit tenant classification, composite keys/foreign keys and RLS policies before they are exposed.
