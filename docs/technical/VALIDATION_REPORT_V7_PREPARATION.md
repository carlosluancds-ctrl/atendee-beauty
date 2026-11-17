# Validation report — Starter Kit v7 preparation

Date: 2026-11-17
Base: Starter Kit v6

## Changes

- Added `.github/workflows/postgres18-persistence.yml` to start a disposable PostgreSQL 18.7 container on a GitHub-hosted Ubuntu runner, bound to loopback only.
- The workflow applies the existing restricted role template and sample migration, generates temporary per-job administrator and login passwords, and runs the EF Core/Npgsql persistence runner as `atendee_app_login`.
- Added a persistence assertion that the same PostgreSQL backend is reused safely after the synthetic rollback, complementing the existing A/B pool-reuse assertion.
- Added `docs/POSTGRES18_CI_EXECUTION_V1.md` and updated the next-step runbook.

## Checks performed in this environment

- Archive contents were extracted and inspected.
- Workflow and source changes were inspected statically.
- Official GitHub Actions release/documentation sources were checked for `actions/checkout@v7` and `actions/setup-dotnet@v6`; Docker Hub was checked for the `postgres:18.7-bookworm` official-image tag.
- `global.json` pins .NET SDK 10.0.100 with `latestFeature` roll-forward; the workflow installs 10.0.404, matching the previously recorded successful build environment.
- The current execution environment does not contain `dotnet` or `docker`; therefore no build, C# check runner, PostgreSQL service, migration, SQL policy, or Npgsql integration test was executed for v7.

## Status

**Prepared, not runtime-validated.** The PostgreSQL 18.7/RLS/pool gate remains open until the workflow is actually run on a Linux CI runner and all checks pass. The recorded v6 build/domain validation remains historical evidence for v6, not proof that v7 compiles.

No connection was made to the user's Windows Server. No server, database, Docker, firewall, volume, or production configuration was changed.
