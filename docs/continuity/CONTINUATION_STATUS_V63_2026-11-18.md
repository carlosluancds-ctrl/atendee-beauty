# Atendee Beauty — Continuity Status V63 (2026-11-18)

## Outcome

V63 full-stack browser E2E is implemented and the complete CI workflow passed. The browser now exercises the real React portal against the real ASP.NET Core API, hosted through temporary HTTPS Kestrel, backed by the disposable PostgreSQL 18.7 database provisioned by CI.

## Implemented

- Added `tests/Atendee.Api.HttpChecks/BrowserE2eRunner.cs`:
  - Starts the real API with `WebApplicationFactory` and Kestrel on an ephemeral loopback port.
  - Creates a short-lived self-signed localhost certificate and a temporary Data Protection key ring.
  - Provisions isolated synthetic Owner, Manager, Professional and Receptionist identities in the disposable test database.
  - Uses the existing first-Owner provisioning service; no production provisioning path or backdoor was added.
  - Starts the portal's same-origin development proxy and runs the separate full-stack Playwright configuration.
  - Removes temporary server/key-ring resources on completion.
- Added `web/atendee-portal/tests/e2e/fullstack.spec.mjs`:
  - Owner signs in with TOTP, verifies secure session-cookie attributes, and creates a service through the actual UI/API.
  - Manager completes required MFA enrollment, creates a Professional profile and assigns the service.
  - Professional signs in and sees only their own assigned service at `/my-services`.
  - Receptionist sees the team read-only; the UI does not request eligible-members discovery and a direct API request is denied with HTTP 403.
  - Verifies unsafe browser mutations include the antiforgery header.
- Added `playwright.fullstack.config.mjs` and the `test:e2e:fullstack` npm script. The existing mocked browser suite remains separate and unchanged in purpose.
- CI runs full-stack browser checks from the existing API HTTP integration fixture after the disposable PostgreSQL database and restricted roles are provisioned.
- Corrected middleware ordering: `UseAuthentication` now establishes the claims principal before antiforgery validates a user-bound request token. The middleware still fails closed for every unsafe request.
- Updated HTTP checks to fetch a fresh antiforgery request token after login, MFA completion and logout, matching the portal's existing token-refresh behavior.

## Validation

Complete CI passed on commit `e106c5926b93168ca4e0c9b7b2a6e762fc6606a4`:

- Workflow run 44319685006: success — https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44319685006
- Frontend: `npm ci`, `npm audit`, 2 frontend unit tests, TypeScript/build, Chromium install and 8 mocked Playwright tests passed.
- .NET: locked restore, Release build and 26/26 domain checks passed.
- Database: disposable PostgreSQL 18.7, 10/10 persistence/RLS checks passed.
- Identity: 49/49 Identity/TOTP/tenant-context checks passed.
- API: HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks passed.
- Full-stack browser: 1 Playwright test passed against the real Kestrel API and disposable PostgreSQL database.
- No migrations, production credentials, production identities, deployments or production data changes were used.

## Coverage boundary

- The 8-test `portal.spec.mjs` suite continues to use deterministic mocked API responses and proves browser/UI behavior only.
- The 1-test `fullstack.spec.mjs` suite exercises real API, real session cookies, real antiforgery validation and real PostgreSQL-backed workflows.
- Existing API HTTP, Identity and PostgreSQL/RLS suites remain the authority for backend authorization and tenant isolation. Full-stack browser coverage supplements rather than replaces them.

## Next step

V64: extend the real-browser full-stack suite with explicit two-tenant negative cases and authenticated antiforgery/session-transition regression cases. Keep the API/RLS integration checks authoritative for tenant isolation; do not weaken backend authorization or add production-only test switches.

## Repository and safeguards

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; `main` remains untouched.
- Preserve V41 first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Local `C:\\Temp\\atendee-work` is stale. Use GitHub branch and CI as source of truth.
- No deployment, SES configuration, billing/trial activation or production data changes are authorized.
