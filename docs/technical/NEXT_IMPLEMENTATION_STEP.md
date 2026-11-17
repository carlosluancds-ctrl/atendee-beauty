# Next implementation step — v7

**Local validation recorded in milestone V19:** locked restore and Release build passed with .NET SDK 10.0.404; all 15 domain checks passed. The persistence runner compiled but has not been run against PostgreSQL 18.7. This does not replace the GitHub Actions runtime gate.

1. Push `Atendee_Beauty_Starter_Kit_v7` as the repository root (or move `.github/workflows/postgres18-persistence.yml` into the root of the existing application repository).
2. Run the `PostgreSQL 18 persistence integration` GitHub Actions workflow. Do not treat the source-only preparation as a passing test.
3. Review the complete logs. Require locked restore, Release build, domain checks and every persistence integration check to pass; the persistence runner must not report `SKIP`.
4. Fix every role, migration, RLS, EF Core or connection-pool failure before proceeding. Preserve the CI run URL/ID and its date in the next continuity checkpoint.
5. Only after that runtime gate passes, implement authentication/MFA and the production `ICurrentTenantContext`; derive tenant exclusively from the authenticated operational account's single tenant membership.
6. Then add customer/service/professional/appointment entities and migrations. Each tenant table must include `tenant_id`, composite tenant-aware keys/foreign keys, RLS and cross-tenant tests.

The current Windows Server must not be switched to Linux containers or have PostgreSQL installed merely to run the lab. The existing PostgreSQL 14 service remains unused. No production server changes are authorized by this test plan.
