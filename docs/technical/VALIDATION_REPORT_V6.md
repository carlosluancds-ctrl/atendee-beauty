# Validation report — Starter Kit v6

Date: 2026-11-17
Validated on: `runnervm9zap3` — Windows Server 2022, .NET SDK 10.0.404.

## Build and restore

- Complete solution restore: successful.
- `dotnet restore Atendee.sln --locked-mode`: successful using committed `packages.lock.json` files.
- `dotnet build Atendee.sln --no-restore --configuration Release`: successful; all six projects built (Domain, Application, Infrastructure, API, Domain.Checks and Persistence.Checks), with **0 warnings and 0 errors**.
- Release configuration mappings in `Atendee.sln` were reviewed and corrected for both check projects.

## Domain checks

- The dependency-free domain check runner completed successfully: **15/15 checks passed**.
- Covered tenant name/slug normalization, pending initial status, non-empty tenant ID, invalid slug/time-zone rejection, UTC timestamp requirement, rejection of default tenant IDs and undefined roles, display-name length/control-character validation, single-tenant association, activation and deactivation.

## EF Core / Npgsql persistence adapter

- The full Infrastructure and Persistence.Checks projects compile in Release with warnings treated as errors.
- Resolved package graph: `Microsoft.EntityFrameworkCore` 10.0.15; `Microsoft.EntityFrameworkCore.Relational` 10.0.15; Npgsql EF Core provider 10.0.3; Npgsql driver 10.0.3.
- NuGet lock files were generated from the resolved dependency graph; locked-mode restore of the complete solution succeeded.
- `dotnet list package --vulnerable --include-transitive` reported no known vulnerable packages in Infrastructure and Persistence.Checks using the configured NuGet sources on 2026-11-17. This is a point-in-time result, not a future guarantee.
- Persistence integration runner compiles successfully with 0 warnings and 0 errors.
- Running the integration runner without `ATENDEE_TEST_CONNECTION_STRING` produced `SKIP` and exited without connecting to a database.

## PostgreSQL runtime testing — not yet verified

- **No PostgreSQL 18.7 instance was available during this validation.** The RLS policies, role grants, EF Core queries, connection-pool reuse, cross-tenant isolation and rollback recovery have NOT been executed against a real PostgreSQL database.
- The Windows host's Docker Engine reports `OSType=windows`; Linux containers are unavailable. The project deliberately does not switch Docker modes or install a database on this host.
- PostgreSQL 14.24 binaries were found, but the `postgresql-x64-14` service remained `Stopped` and `Disabled`. PostgreSQL 14 is end-of-life and is not used by this project.
- The temporary validation workspace was removed after the checks. No DB service was started, no ports were opened and no persistent project deployment was performed.

## Not production-ready

- No production authentication/MFA provider or real authenticated `ICurrentTenantContext` exists yet.
- API startup does not activate the persistence adapter; this is intentional until authentication establishes a trusted tenant scope.
- `tenant_notes` is a sample vertical slice, not a business schema. Customer, service, professional, appointment, conversation, campaign and billing tables still need schema, composite foreign keys, RLS policies and tests.
- The transaction-local custom GUC is context, not a cryptographic credential. Arbitrary SQL executed under the runtime role can potentially set a different tenant. RLS does not mitigate SQL injection. Parameterized queries, strict API authorization and minimal privileges remain mandatory.

Do not deploy this scaffold publicly or process real customer data until PostgreSQL 18.7 integration tests, authentication/authorization, schema coverage, backup/restore verification and an independent security review are complete.
