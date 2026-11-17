# Validation report — Starter Kit v5

Date: 2026-11-17

## Verified on runnervm9zap3 (Windows Server 2022)

- .NET SDK: 10.0.404.
- NuGet restore succeeded for the complete solution.
- `dotnet build Atendee.sln --no-restore --configuration Release`: success; all six projects built (Domain, Application, Infrastructure, API, Domain.Checks and Persistence.Checks), with 0 warnings and 0 errors.
- `dotnet restore Atendee.sln --locked-mode`: success; the package graph restored using the committed lock files.
- Domain invariant checks: 15/15 passed.
- Persistence integration runner compiled. Running it with no `ATENDEE_TEST_CONNECTION_STRING` printed `SKIP` and exited successfully without opening a database connection.
- Resolved versions: EF Core 10.0.15, EF Core Relational 10.0.15, Npgsql EF provider 10.0.3 and Npgsql 10.0.3.
- NuGet vulnerability audit (`dotnet list package --vulnerable --include-transitive`) reported no vulnerable packages for Infrastructure and Persistence.Checks using configured sources on 2026-11-17.

## Not verified

- No PostgreSQL 18.7 connection, migration, RLS or connection-pool integration test has run. The current host's Docker engine is in Windows-container mode and the PostgreSQL 14 service remains stopped/disabled.
- The integration runner is ready for a disposable PG 18.7 test database, but a real run with `atendee_app_login` and pool size 1 is still required.
- The API still has no production authentication provider or implementation of `ICurrentTenantContext`; database registration is intentionally not wired into API startup.
- No business table other than the sample `tenant_notes` table has a migration or RLS policy.

The no-vulnerability result is a point-in-time audit of the configured NuGet sources, not a guarantee against future advisories. Do not deploy this scaffold or process real data until the PostgreSQL integration tests and authentication/authorization are complete.
