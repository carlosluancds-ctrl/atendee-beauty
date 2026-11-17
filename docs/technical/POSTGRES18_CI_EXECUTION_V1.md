# PostgreSQL 18.7 integration in Linux CI — v1

## Purpose

Provide a repeatable, disposable Linux CI path for the EF Core/Npgsql persistence checks. This validates the sample tenant transaction boundary, PostgreSQL role grants, RLS policy behavior, and physical connection reuse without changing the current Windows Server.

## What the workflow does

The workflow `.github/workflows/postgres18-persistence.yml`:

1. Runs on a GitHub-hosted Ubuntu runner and starts a disposable `postgres:18.7-bookworm` container bound only to `127.0.0.1:5432`.
2. Restores the committed NuGet dependency graph in locked mode and builds the complete solution in Release.
3. Runs the dependency-free domain invariant checks.
4. Creates a new `atendee_rls_lab` database for this job only.
5. Applies `db/security/001_roles_template.sql` as the temporary PostgreSQL administrator.
6. Generates temporary random passwords for `atendee_app_login` and `atendee_migrator`; applies the sample migration as `atendee_migrator`.
7. Runs `Atendee.Persistence.Checks` as `atendee_app_login` with Npgsql pool size 1.
8. Removes the ephemeral CI service when the job ends.

The PostgreSQL administrator, app-login and migrator passwords are generated randomly for each job. They are not written to repository files or logs. The connection string is passed to the integration runner through the CI job environment and the disposable container is removed even when a preceding step fails.

## Enable it

Use `Atendee_Beauty_Starter_Kit_v7` as the repository root, preserving its `.github/workflows` directory. Push the folder to a private repository or copy the workflow into the root of the existing code repository. GitHub Actions must be enabled for that repository.

The workflow has no dependency on the Atendee production VPS, its existing Docker installation, n8n, WhatsApp account, or production database. The test container publishes its port only on the CI runner loopback interface.

## Passing criteria

The job must complete all of the following without `SKIP`:

- locked-mode restore succeeds;
- full Release build succeeds with the repository's warnings-as-errors policy;
- domain checks pass;
- persistence checks report every check as `PASS` and exit with code 0.

In particular, the persistence checks require:

- tenant A reads its own row;
- tenant B cannot read tenant A's row by known UUID;
- tenant B cannot insert a row owned by A;
- direct table access as `atendee_app_login` outside the transaction runner is denied;
- the physical PostgreSQL backend is reused with pool size 1 across A/B scopes;
- rollback is performed after the synthetic operation failure;
- the same physical backend can be safely reused after rollback;
- tenant A still reads its original row after the B and rollback scenarios.

## Security and limitations

- This is a disposable integration database containing synthetic test data only.
- No production host, firewall, persistent volume, or service is changed.
- The CI workflow validates the sample `tenant_notes` vertical slice only. It does not certify the future customer, service, professional, appointment, conversation, campaign, WhatsApp, AI, billing, or subscription schemas.
- A passing job does not replace authentication/MFA, authorization, tenant identity resolution, full RLS coverage, backup/restore verification, or an independent security review.
- RLS context via a custom PostgreSQL setting is defense in depth, not protection against arbitrary SQL execution or SQL injection.

## Current execution status

Prepared in Starter Kit v7. The workflow has **not yet been run** in GitHub Actions or another Linux CI runner. It is not valid to claim PostgreSQL runtime/RLS verification until an actual CI run succeeds and its logs are reviewed.

## Tooling references checked for this workflow

- `actions/checkout@v7` and `actions/setup-dotnet@v6` follow the current official action documentation/release lines checked on 2026-11-17.
- `postgres:18.7-bookworm` is listed by Docker Hub as a tag of the official PostgreSQL image, checked on 2026-11-17.
- .NET SDK `10.0.404` matches the SDK version recorded in the v6 Windows Server build validation. The repository `global.json` requests 10.0.100 and permits the latest feature band.
