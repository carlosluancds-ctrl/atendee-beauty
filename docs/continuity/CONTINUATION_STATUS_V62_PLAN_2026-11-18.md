# Atendee Beauty — V62 Browser E2E Plan (2026-11-18)

## Current state
V61 is approved. This document records the inspected implementation constraints and the V62 test design; it does not claim browser tests have been implemented or run.

## Inspected portal architecture
- React 19 + TypeScript, bundled with esbuild.
- Routes are selected in `web/atendee-portal/src/App.tsx`: `/activate`, `/team`, `/my-services`, `/services`, and the login fallback.
- `web/atendee-portal/scripts/dev-server.mjs` serves the built portal on loopback and proxies `/api/*` to `ATENDEE_API_PROXY_TARGET`.
- The portal obtains an antiforgery request token from `GET /api/v1/auth/antiforgery` and sends it in `X-CSRF-TOKEN` for unsafe API calls.
- The API requires HTTPS redirection and uses Secure, HttpOnly, SameSite=Strict `__Host-` cookies for authentication and antiforgery.
- Current portal scripts are `dev`, `build`, and `test`. Current tests use Node's built-in test runner and test activation helper functions only.
- The portal package has React, React DOM, TypeScript, esbuild and React type packages. No browser automation framework is declared.
- CI is `.github/workflows/postgres18-persistence.yml`. It uses `npm ci`, runs `npm audit`, frontend tests/build, then provisions a disposable PostgreSQL 18.7 database and runs persistence, Identity/TOTP and API HTTP checks.

## Proposed V62 coverage
1. Browser smoke tests for login, MFA challenge, first-time MFA enrollment/recovery acknowledgement, and logout.
2. Role-specific navigation and rendered states for Owner, Manager, Receptionist and Professional.
3. Team/professional browser flows: eligible-member discovery and profile creation for Owner/Manager; assigned-services view for Professional.
4. Negative UI/API cases for Receptionist access and cross-tenant data visibility.
5. Antiforgery behavior and cookie/session handling through the actual same-origin portal proxy.

## Test architecture decision
- Keep the existing API HTTP and RLS integration checks authoritative for backend authorization and tenant isolation.
- Use isolated, synthetic test data only. No production identities, secrets, SES, billing or deployed services.
- Browser tests must not use production URLs. Prefer the existing CI PostgreSQL fixture and a local API + portal process, or explicitly document any API mocking boundary if the browser suite is frontend-only.
- Do not present mocked API browser tests as proof of backend authorization. Keep the distinction explicit.
- Add the browser framework only with a reproducible dependency lockfile. Do not hand-edit or guess npm integrity hashes. Preserve `npm ci` and `npm audit`.
- CI must install a pinned browser/runtime version and execute the browser suite deterministically. Preserve the existing PostgreSQL/RLS/Identity checks.

## Blockers / decisions before implementation
- The current CI API HTTP check is a standalone executable that creates synthetic identities and exercises the API through `WebApplicationFactory`; it does not expose a persistent API process or seed browser-ready login credentials.
- A browser suite that exercises real authentication therefore needs a deliberate test-host/seed handoff, or a dedicated test harness that starts the API and seeds isolated identities while the browser test runs.
- Decide whether V62 first delivers frontend-only browser smoke tests with explicitly mocked API responses, or a full-stack browser harness using the disposable PostgreSQL fixture. Full-stack coverage is preferred for real MFA/session and tenant-boundary claims.
- Generate and verify dependency lock changes with npm itself in a controlled checkout; never fabricate lock metadata.

## Repository safeguards
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; keep `main` untouched.
- Preserve V41 first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- No deploy, SES setup, billing/trial activation or production data changes without explicit authorization.
