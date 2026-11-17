# Validation report — Starter Kit v2

**Date:** 2026-11-17  
**Host:** `runnervm9zap3` (Windows Server 2022)  
**Scope:** build of the .NET scaffold and localhost health endpoint only.

## Passed

1. SDK selection: `10.0.404` selected from `global.json` (`10.0.100`, `latestFeature`).
2. Release build of `src/Atendee.Api/Atendee.Api.csproj`, including project references to Domain, Application and Infrastructure: succeeded; 0 warnings, 0 errors.
3. API smoke test bound only to `http://127.0.0.1:5127`: `GET /health/live` returned HTTP 200 with `{"status":"ok"}`.
4. Response headers observed: `X-Content-Type-Options: nosniff` and `Cache-Control: no-store`.
5. Temporary working directory and test process were removed/stopped; port 5127 had no listener afterward.

## Not tested / blockers

- PostgreSQL RLS scripts were not executed. The host has PostgreSQL 14 binaries/service only; the service was stopped/disabled and `psql` was not on PATH. The project baseline is PostgreSQL 18, so PostgreSQL 14 was not started or used.
- No xUnit, NUnit, MSTest or `Microsoft.NET.Test.Sdk` packages were found in the local NuGet cache. No unit/integration test runner is configured yet.
- No authentication, authorization, database connection, RLS integration, migrations, tenant-scoped API or production deployment exists in this scaffold.
- No internet package restore or vulnerability scan was possible from this execution environment.

## Environment change policy

The build used a temporary source copy under the user Documents folder. It was deleted after the checks. No PostgreSQL service, Docker service, firewall rule, IIS site, port binding beyond loopback smoke test, production directory or persistent application service was changed.

## Interpretation

This result proves only that the current starter scaffold compiles and its health endpoint responds locally. It does not prove tenant isolation, application security, production readiness, database compatibility or availability under load.
