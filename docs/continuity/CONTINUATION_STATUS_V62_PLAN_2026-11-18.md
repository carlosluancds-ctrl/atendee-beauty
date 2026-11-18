# Atendee Beauty — V62 Browser E2E Plan (2026-11-18)

> **Status: implemented and approved.** The final outcome and CI evidence are recorded in `docs/continuity/CONTINUATION_STATUS_V62_2026-11-18.md`. This file is retained as the original architecture/design note.

## Architecture findings
- React 19 + TypeScript, bundled with esbuild.
- Routes in `web/atendee-portal/src/App.tsx`: `/activate`, `/team`, `/my-services`, `/services`, and login fallback.
- `web/atendee-portal/scripts/dev-server.mjs` serves the built portal and proxies `/api/*` to `ATENDEE_API_PROXY_TARGET`.
- Unsafe portal API calls obtain an antiforgery token from `GET /api/v1/auth/antiforgery` and send `X-CSRF-TOKEN`.
- Existing CI provisions disposable PostgreSQL 18.7 and runs RLS, Identity/TOTP and API HTTP integration checks.
- The existing API HTTP executable seeds synthetic identities inside `WebApplicationFactory`, but does not expose a persistent API process or browser-ready credentials.

## V62 implementation decision
V62 deliberately delivered frontend/browser E2E tests with deterministic mocked API responses first. This exercises the actual React UI and browser interactions without pretending that mocks prove backend authorization. The authoritative backend tests remain the existing real API HTTP, Identity/TOTP and PostgreSQL/RLS suites.

The 8 browser cases cover login/MFA challenge, first-time MFA enrollment and recovery acknowledgement, Owner/Manager professional profile creation and service assignment, Receptionist read-only team/catalogue views, Professional assigned services, and Owner service editing.

Playwright is pinned at `1.65.1` with an npm-generated lockfile. CI installs Chromium and runs the browser suite. The initial Playwright 1.56.1 attempt failed on Ubuntu 26.04 because that version did not support the runner image; upgrading to 1.65.1 resolved it.

## Next decision: V63 full-stack browser harness
- Reuse the disposable PostgreSQL provisioning already used in CI.
- Inspect and reuse the API HTTP test fixture, `WebApplicationFactory`, Identity/TOTP helpers and provisioning pathways.
- Create a controlled test-host/seed handoff that runs the real ASP.NET API and portal while the browser executes.
- Use only isolated synthetic users, memberships, tenants and secrets.
- Do not create production backdoors, weaken MFA, bypass antiforgery, relax RLS or treat mocked browser tests as authorization proof.

## Safeguards
- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: `security/csrf-webhook-antiforgery-v23`; keep `main` untouched.
- Preserve V41 first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- No deploy, SES setup, billing/trial activation or production data changes without explicit authorization.
