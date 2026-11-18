# Atendee Beauty — Continuity Status V47 (2026-11-18)

## Scope
Second frontend increment: login, MFA challenge, MFA enrollment and recovery-code experience, integrated with the existing ASP.NET Core authentication contracts. No backend route or security policy was changed in V47.

## Previous verified gates
- V45 implementation commit bd16f1d409c3a3571d7d6d5eab8ab39a328474ab passed full GitHub Actions run 44282286929.
- V46 continuity checkpoint commit d96a50e676a34bfd9b595bc6ef1128b6826e8edd passed full GitHub Actions run 44282678565.
- Both exact-SHA runs included Node frontend audit/test/build and PostgreSQL 18.7 backend integration checks.

## V47 frontend implemented
New route behavior in web/atendee-portal:
- /activate — account activation page.
- /login (and root fallback) — login and MFA onboarding page.
- Login posts email/password to POST /api/v1/auth/login.
- Handles backend states:
  - authenticated: existing session cookie issued by backend;
  - mfa_required: displays TOTP or recovery-code challenge;
  - mfa_setup_required: uses the short-lived MFA setup cookie and starts enrollment.
- MFA challenge posts code/useRecoveryCode to POST /api/v1/auth/mfa/challenge.
- MFA enrollment uses:
  - POST /api/v1/auth/mfa/enrollment/start to retrieve shared key and authenticator URI;
  - POST /api/v1/auth/mfa/enrollment/verify to confirm TOTP;
  - POST /api/v1/auth/mfa/enrollment/cancel to abandon setup.
- Recovery codes are displayed once and kept only in component memory until the user confirms they have saved them.
- For an identity with no salon membership, successful MFA enrollment shows the pending-Owner state and does not assume or create an authenticated application session. The existing offline V41 first-Owner command remains required.
- For an existing eligible membership, the backend issues the authenticated session only after successful MFA enrollment.
- All unsafe API requests obtain an antiforgery token and send X-CSRF-TOKEN; requests use same-origin credentials and no-store.
- No session, activation, MFA, recovery or antiforgery token is persisted in localStorage/sessionStorage.
- No backend endpoints, cookie policies, or authorization policies were modified.

## Local validation observed
Executed in web/atendee-portal on Node.js 22.23.5:
- npm ci: passed; 0 vulnerabilities.
- npm test: 2/2 passed.
- npm run build: passed (strict TypeScript check + esbuild bundle).
- npm audit: 0 vulnerabilities.
- Static development-server smoke: /login, /activate and /assets/app.js returned HTTP 200; compiled JavaScript contains login, MFA challenge and activation views; CSP header present.
- The local dev API proxy was not tested against a running ASP.NET API.
- V47 commit and full CI are pending push; do not claim the new login/MFA frontend has passed CI until the exact V47 SHA completes successfully.

## Backend contracts inspected and reused
- POST /api/v1/auth/login: 200 authenticated, 202 mfa_required, or 202 mfa_setup_required; generic 401 on invalid credentials/account state.
- POST /api/v1/auth/mfa/challenge: TOTP or recovery-code challenge; full application cookie only after successful challenge.
- POST /api/v1/auth/mfa/enrollment/start: returns sharedKey and authenticatorUri under the short-lived MFA setup cookie.
- POST /api/v1/auth/mfa/enrollment/verify: returns one-time recoveryCodes; for no-membership identity returns mfa_enrolled_membership_pending and does not issue the application cookie.
- POST /api/v1/auth/mfa/enrollment/cancel: clears the MFA setup cookie.
- GET /api/v1/auth/antiforgery: returns requestToken and stores antiforgery cookie.
These are existing backend routes; no new route was invented for V47.

## Boundaries and remaining work
- Frontend is onboarding-only; salon dashboard, booking, customer management and billing views are not implemented in this increment.
- Production IIS URL Rewrite/ARR same-origin proxy for /api/*, HTTPS and deployment are not configured.
- SES sender domain/address verification, Easy DKIM, SPF/DMARC, SES production access and IAM credentials remain external prerequisites.
- Migration 011 and restricted provisioner login have not been applied to any production/shared database.
- Platform Admin store/MFA/break-glass, WebAuthn/passkey, production Data Protection protector, distributed rate limiting and trusted proxy configuration remain open.
- No real email, production data, DNS change, server change or deployment occurred.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23; main remains untouched.
- Last implementation commit with full CI verified: bd16f1d409c3a3571d7d6d5eab8ab39a328474ab.
- V47 frontend login/MFA implementation is prepared locally and awaiting commit/CI.
- Preserve all previous versioned continuity files.

## Next step
Push V47 and verify frontend npm ci/audit/test/build plus the complete PostgreSQL 18.7 backend workflow for the exact commit. Fix any failures. After the CI gate passes, perform a focused browser-level review of activation, login, TOTP challenge, recovery-code challenge and pre-membership MFA enrollment, then proceed to production hosting prerequisites only when the owner supplies authorized server/AWS/domain access.
