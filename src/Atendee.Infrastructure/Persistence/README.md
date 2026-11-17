# Persistence adapter — initial implementation

The first Npgsql/EF Core adapter is now present:

- `TenantDbContext` with immutable scoped tenant capture, query filter and SaveChanges ownership checks.
- `NpgsqlTenantTransactionRunner` which resets connection state, assumes the fixed restricted role, sets parameterized transaction-local tenant context, saves and commits.
- `EfTenantNoteStore` as a small vertical slice; it never accepts tenant ID from the caller.
- SQL role template and a tenant-notes migration.

Package versions are pinned in the project file. EF Core DbContext pooling is intentionally not enabled.

This code has not yet been integration-tested against PostgreSQL 18.7. Do not use it with real data or deploy it publicly until the isolated PostgreSQL tests pass and authentication/authorization are implemented.
See `docs/TENANT_DATABASE_CONTEXT_V1.md` and `docs/PERSISTENCE_VALIDATION_PLAN_V1.md`.
