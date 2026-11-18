# Atendee Beauty — Continuity Status V46 (2026-11-18)

## Scope
Verification checkpoint after the V45 React/TypeScript activation frontend increment. No additional product behavior or production configuration was changed in V46.

## V45 commit and CI
- Frontend-inclusive implementation commit: bd16f1d409c3a3571d7d6d5eab8ab39a328474ab.
- GitHub Actions run 44282286929 passed for the exact V45 SHA:
  https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44282286929
- Frontend steps all passed:
  - Node.js 22 setup;
  - npm ci;
  - npm audit --audit-level=moderate;
  - Node built-in activation-token/password tests;
  - strict TypeScript check and esbuild production bundle.
- Backend steps all passed in the same run:
  - locked .NET restore and Release build;
  - domain checks;
  - PostgreSQL 18.7 role/migration provisioning;
  - EF Core/RLS persistence checks;
  - Identity/TOTP/activation concurrency checks;
  - API HTTP authentication, antiforgery and no-session checks.
- Local frontend validation also passed: npm ci, 2/2 tests, production build, npm audit with 0 vulnerabilities, and static HTTP smoke returning 200 with CSP and Referrer-Policy headers.

## Current implementation
Backend:
- Offline operational identity create/reissue command.
- Amazon SES v2 integration using AWS SDK credential chain.
- One-time 256-bit activation token, 30-minute expiry, SHA-256 hash only, re-issue rotation.
- POST /api/v1/auth/activate sets password and confirms email atomically; no session, salon or membership is created.
- Dedicated provisioner database role and migration 011.
- V41 first-Owner provisioning remains unchanged and is still the only way to create the first salon/Owner membership after confirmed email and MFA.

Frontend:
- web/atendee-portal is a React 19 + TypeScript activation SPA.
- Reads the token from URL fragment and removes it from visible URL/history.
- Requests antiforgery token/cookie and posts token+password using same-origin credentials and X-CSRF-TOKEN.
- Does not persist activation token, password or antiforgery token in browser storage.
- Clears token/password from component state after success and explains that activation did not issue a session; MFA enrollment remains required.
- Uses esbuild for production static output; dependency audit is clean.
- Local Node development proxy is only for development.
- IIS web.config provides static hosting, security headers and SPA fallback. Production IIS URL Rewrite/ARR reverse proxy for /api/* remains an external host configuration task.

## Remaining work and boundaries
- Frontend login and MFA enrollment pages are not yet implemented.
- Existing backend login/MFA endpoints and their contracts must be inspected before building those screens.
- Same-origin HTTPS hosting and IIS URL Rewrite/ARR API proxy are not configured.
- SES sender domain/address verification, Easy DKIM, SPF/DMARC, SES production access and IAM credentials remain external prerequisites.
- Migration 011 and restricted provisioner login have not been applied to any production/shared database.
- Platform Admin store/MFA/break-glass, WebAuthn/passkey, production Data Protection protector, distributed rate limiting and trusted proxy configuration remain open.
- No real email, production data, DNS change, server change or deployment occurred.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Work branch: security/csrf-webhook-antiforgery-v23.
- main remains untouched.
- Latest implementation commit with verified full CI: bd16f1d409c3a3571d7d6d5eab8ab39a328474ab.
- Preserve previous versioned continuity files.

## Next step
Continue the frontend onboarding experience with the existing login and MFA setup APIs. Inspect their exact request/response contracts, cookies, CSRF requirements, and MFA challenge/enrollment transitions before implementing UI. Maintain same-origin cookies, no browser token storage, and no session before successful MFA where the backend requires it. Run frontend tests/build/audit and the full PostgreSQL 18.7 workflow for each increment. Do not configure SES or deploy without the external prerequisites and explicit release gates.
