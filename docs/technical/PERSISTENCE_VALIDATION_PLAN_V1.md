# Persistence validation plan — v1

## Local/static checks

- Restore dependencies from the lockfile.
- Build the full solution in Release with warnings as errors.
- Run the dependency-free domain checks.
- Inspect generated EF model and migration SQL; review table owner, runtime privileges and RLS flags.
- Scan code and repository for secrets and connection strings.

## PostgreSQL integration checks (disposable PostgreSQL 18.7 only)

Use an isolated test database and separate login roles. Never run destructive scripts against production or an existing user database.

1. Apply role template as DBA, provision test-only credentials out of band.
2. Apply migration as `atendee_migrator`.
3. Connect as `atendee_app_login` with `Maximum Pool Size=1` to force physical connection reuse.
4. Create a scoped service provider per tenant; resolve the `ICurrentTenantContext`, `TenantDbContext`, store and runner only after setting the test tenant identity.
5. As tenant A, create a synthetic note and list it.
6. Dispose tenant A scope; create a separate tenant B scope on the same Npgsql pool; verify A's note is not visible and cannot be modified/deleted, even by known UUID.
7. Return to A with another fresh scope and verify its note is still present and unchanged.
8. Run direct query without runner; it must fail due to login-role privileges, not accidentally inherit runtime privileges.
9. Run with missing tenant, invalid tenant, wrong role membership, wrong schema grants, and an exception in callback; each must fail closed and release the connection.
10. Alternate tenants repeatedly with pool size 1 and verify no GUC or role leakage.

## Not yet validated

No PostgreSQL 18 server was installed or launched on the current Windows Server. The Linux Docker harness is still not executable on this host because Docker reports Windows-container mode. Until the integration tests run on a separate Linux test host/CI runner, the transaction/pool/RLS combination remains unverified.

## Executable integration runner

After provisioning a disposable PostgreSQL 18.7 database with the role template and migration, set `ATENDEE_TEST_CONNECTION_STRING` for `atendee_app_login` and run:

```powershell
dotnet run --project .\tests\Atendee.Persistence.Checks\Atendee.Persistence.Checks.csproj --configuration Release
```

The runner sets connection pool maximum size to one and checks A/B scope reuse, known-UUID reads, RLS rejection of cross-tenant insert, denial of direct table access outside the runner and rollback cleanup. Without the environment variable, it exits with `SKIP` and does not connect.
