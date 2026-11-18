# Atendee Beauty — Continuity Status V45 (2026-11-18)

## Scope
Frontend increment: first React/TypeScript activation experience for the operator-issued operational identity flow. The V44 backend remains the source of truth. This increment does not introduce public registration, login/session issuance, tenant creation, or first-Owner provisioning.

## Verified backend gate
- Backend commit: b66e1e7ae3fc794891177dbeb89bfd7f3c2d7ef0.
- The initial V44 commit 92fadfab31587eba3672bb948f3bf03223a6c4ce built successfully, and its PostgreSQL persistence tests passed, but the Identity integration test container failed during service-provider validation because its isolated provisioner container did not register ASP.NET authentication services.
- Fixed the test container by adding AddAuthentication().AddIdentityCookies().
- GitHub Actions run 44279908188 passed for the exact backend fix commit b66e1e7ae3fc794891177dbeb89bfd7f3c2d7ef0:
  - locked .NET restore and Release build;
  - domain checks;
  - PostgreSQL 18.7 startup and restricted role/migration provisioning;
  - EF Core/PostgreSQL RLS checks;
  - Identity, TOTP, tenant-context and activation token checks;
  - API HTTP authentication, antiforgery, activation and no-session checks.
- V41 first-Owner provisioning remains unchanged.

## Frontend implemented
New project: web/atendee-portal.
- React 19 + TypeScript, static build using esbuild; no Vite/Vitest dependency tree.
- Activation page accepts the one-time token only from the URL fragment and removes the fragment from the visible URL/history before rendering.
- Requests GET /api/v1/auth/antiforgery, retains the antiforgery cookie, and submits POST /api/v1/auth/activate with same-origin credentials and X-CSRF-TOKEN.
- Password pre-validation mirrors the API's 12-character minimum, 4 unique characters and 1024-character maximum; server validation remains authoritative.
- The page never writes the token, password or antiforgery request token to localStorage/sessionStorage.
- On successful activation, clears the token/password from React state and explicitly tells the user that activation did not create a session and MFA enrollment remains required.
- Generic invalid/expired/replaced/replayed link response.
- Added a small Node built-in development server with an optional same-origin /api proxy for local development only.
- Added IIS static hosting web.config with SPA fallback and security headers. Production IIS URL Rewrite/ARR reverse proxy for /api/* must still be configured to the ASP.NET API on the same HTTPS origin; that host-level configuration was not changed.
- No login UI yet. Existing backend login and MFA APIs remain available, but this increment is only the activation page.

## Local validation observed
Executed from web/atendee-portal on Node.js 22.23.5:
- npm ci: passed.
- npm test: 2/2 passed.
- npm run build: passed (strict TypeScript check + esbuild production bundle).
- npm audit: 0 vulnerabilities.
- Local static smoke test: GET / returned HTTP 200 and included Content-Security-Policy and Referrer-Policy headers.
- The local API proxy was not exercised against a running ASP.NET API in this environment.
- GitHub Actions for the frontend-inclusive commit is pending after push; no CI result is claimed yet.

## Security and deployment boundaries
- Activation token stays in the URL fragment only until the app reads it, then is removed from browser history. Referrer policy is no-referrer.
- Browser requests are same-origin; no permissive CORS, bearer token storage, or client-side session creation.
- The page's CSP disallows third-party scripts, fonts, and network origins.
- No real email, AWS API call, production database, DNS, server configuration, or deployment was used.
- SES sender verification, DKIM/SPF/DMARC, production access and IAM credentials remain external prerequisites.
- Production static hosting requires the same-origin API reverse proxy and TLS. The sample web.config only provides static hosting/security headers/SPA fallback; ARR reverse proxy setup is an operator/server prerequisite.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23; main remains untouched.
- Previous verified backend commit: b66e1e7ae3fc794891177dbeb89bfd7f3c2d7ef0.
- Frontend-inclusive V45 commit and CI are pending this push.

## Next step
Push V45 to the work branch, verify all frontend and backend CI steps for the exact SHA, fix any failures, then update the continuity status with the observed result. After that, continue the frontend onboarding experience (login/MFA setup) only against the existing backend contracts. Do not configure SES or deploy without the external prerequisites and explicit release gates.
