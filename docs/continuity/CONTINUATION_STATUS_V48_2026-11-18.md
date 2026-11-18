# Atendee Beauty — Continuity Status V48 (2026-11-18)

## Scope
Verification checkpoint for V47 login/MFA onboarding frontend. No new product code or production configuration was changed in V48.

## V47 commit and full CI
- V47 implementation commit: acddf2f545e861d459724284e1906158385caeed.
- GitHub Actions run 44283430559 passed for the exact V47 SHA:
  https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44283430559
- Frontend steps passed:
  - Node.js 22 setup;
  - npm ci;
  - npm audit --audit-level=moderate;
  - Node built-in tests;
  - strict TypeScript check and esbuild production build.
- Backend steps passed:
  - locked .NET restore and Release build;
  - domain checks;
  - PostgreSQL 18.7 startup and restricted role/migration provisioning;
  - EF Core/PostgreSQL RLS checks;
  - Identity, TOTP, activation and tenant-context checks;
  - API HTTP authentication, antiforgery, activation and session-security checks.
- Local frontend validation: npm ci, 2/2 tests, build, npm audit with 0 vulnerabilities, and static smoke of /login, /activate and /assets/app.js returning HTTP 200 with CSP.

## Current frontend
- /activate: one-time activation token read from URL fragment, fragment removed from visible URL/history, antiforgery request and password setup.
- /login: email/password login using existing POST /api/v1/auth/login contract.
- mfa_required: TOTP or recovery-code challenge via POST /api/v1/auth/mfa/challenge.
- mfa_setup_required: setup flow via POST /api/v1/auth/mfa/enrollment/start, /verify and /cancel.
- Recovery codes are displayed once and held only in component memory until user acknowledges saving them.
- Pending first Owner completes MFA without receiving an application session; V41 offline provisioning still creates the first salon membership.
- Existing eligible members receive the backend session only after the required MFA factor is completed.
- All unsafe API requests use antiforgery token/header and same-origin cookies. No authentication material is persisted in localStorage/sessionStorage.
- Static build uses esbuild; dependency audit reports zero vulnerabilities.

## Backend status
- V44 operational identity backend remains unchanged and CI-verified.
- Dedicated provisioner role, migration 011, offline create/reissue CLI, SES v2 sender and atomic activation API remain in place.
- V41 first-Owner provisioning remains unchanged.
- No public self-registration or public first-Owner endpoint exists.

## Remaining work and boundaries
- Frontend currently provides onboarding only. Salon dashboard, booking, customer management and billing views are not implemented.
- A browser-level end-to-end review has not yet been performed; local smoke verifies static routes/assets only, and backend HTTP integration tests verify server contracts.
- Production IIS URL Rewrite/ARR same-origin proxy for /api/*, HTTPS and deployment are not configured.
- SES sender identity/domain, Easy DKIM, SPF/DMARC, production access and IAM credentials remain external prerequisites.
- Migration 011 and restricted provisioner login have not been applied to production/shared database.
- Platform Admin store/MFA/break-glass, WebAuthn/passkey, production Data Protection protector, distributed rate limiting and trusted proxy configuration remain open.
- No real email, production data, DNS change, server change or deployment occurred.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Branch: security/csrf-webhook-antiforgery-v23; main remains untouched.
- Latest implementation commit with full CI verified: acddf2f545e861d459724284e1906158385caeed.
- Preserve all previous versioned continuity files.

## Next step
Perform a focused browser-level review of activation, login, TOTP challenge, recovery-code challenge and pre-membership MFA enrollment. Validate that cookies survive the same-origin proxy, that the MFA setup cookie is required only for setup routes, and that the pending Owner path never receives the application cookie. Then document IIS ARR/TLS deployment prerequisites; do not configure production hosting, SES or DNS without authorized external access and explicit release gates.
