# Validation report — Starter Kit v3

**Date:** 2026-11-17  
**Host examined:** `runnervm9zap3` (Windows Server 2022)  
**Scope:** .NET build/smoke checks and current PostgreSQL/Docker capability inspection.

## Passed in the preceding validation stage

- .NET SDK `10.0.404` selected by `global.json`.
- Release build of Domain, Application, Infrastructure and API: 0 warnings, 0 errors.
- Local-only API smoke test: `GET /health/live` returned HTTP 200, `{"status":"ok"}`.
- Security headers `X-Content-Type-Options: nosniff` and `Cache-Control: no-store` observed.
- 15 domain invariant checks passed in a temporary, dependency-free C# check runner. The same 15 checks are now included reproducibly under `tests/Atendee.Domain.Checks`.
- Temporary validation folders and processes were removed; the smoke-test port had no listener afterward.

## Current PostgreSQL / Docker inspection

- PostgreSQL 14.24 binaries are present under `C:\Program Files\PostgreSQL\14\bin`.
- Service `postgresql-x64-14` is `Stopped` and `Disabled`; it was not started or modified.
- The current date is after PostgreSQL 14 end-of-life (12 November 2026). PostgreSQL 14.25 was announced as the final 14.x release; the installed binary is 14.24. Do not use this installation for the project.
- Docker daemon reports `OSType=windows`, with zero containers. The current host cannot run the Linux PostgreSQL official image through this engine mode.
- No PostgreSQL 18 server was installed or run on the Windows Server.

## PostgreSQL RLS test status

- Updated SQL tests and a Docker Compose 18.7 harness were prepared in this Starter Kit.
- **Not executed yet:** this host is in Windows-container mode, and the available PostgreSQL 14 is EOL. The harness intentionally refuses Windows-container mode.
- It must be executed on a separate Linux Docker host or CI runner before relying on RLS behavior.

## No persistent server changes

This stage did not install PostgreSQL, change Docker mode, enable WSL2, start PostgreSQL 14, open firewall ports, create services, modify IIS or change production application directories.

## Additional v4 check-runner validation

- The included dependency-free domain check project was executed on `runnervm9zap3` using SDK `10.0.404`: **15/15 checks passed**.
- The temporary working copy was removed after the run.
- No database or RLS SQL test was executed.
- `scripts/test-rls.sh` passed Bash syntax validation, and `compose.rls.yaml` parsed successfully as YAML in the available validation environment. These static checks do not execute Docker or PostgreSQL.
