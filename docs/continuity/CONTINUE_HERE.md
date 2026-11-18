# CONTINUE HERE — Atendee Beauty (auto-backup updated each step)

**Primary resume context:** read CLAUDE.md, this file, and the highest-numbered docs/continuity/CONTINUATION_STATUS_V*.md before changing code.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Active branch: security/csrf-webhook-antiforgery-v23; never modify main.
- Last backend commit with verified full CI: b66e1e7ae3fc794891177dbeb89bfd7f3c2d7ef0.
- V45 frontend activation increment is prepared in the temporary remote snapshot; its repository commit and frontend-inclusive CI are pending push.

## Completed
- V41: offline first-Owner salon provisioning command. Requires existing identity with confirmed email, MFA enabled, no active lockout and no salon membership; atomically creates PendingVerification salon + active Owner membership. No HTTP endpoint.
- V42: approved separate protected operator command to create the initial operational identity and initiate activation.
- V43: selected Amazon SES API v2 as the default transactional email provider and documented activation design.
- V44: backend activation path implemented: dedicated provisioner role, migration 011, offline create/reissue CLI, SES v2 email sender, activation API, audit events, integration tests and runbook. V41 remains unchanged.
- V44 CI initially exposed missing authentication services in the isolated provisioner test provider. Fixed in b66e1e7ae3fc794891177dbeb89bfd7f3c2d7ef0. GitHub Actions run 44279908188 passed for that exact SHA.
- V45: React/TypeScript activation page in web/atendee-portal; fragment token extraction and history cleanup, antiforgery request, password form, same-origin API calls, success/error states, static build, local development proxy, IIS static-hosting config and Node tests.

## V44 backend details
- No public self-registration, public tenant creation, public account creation route, or public first-Owner endpoint.
- Offline command tools/Atendee.OperationalIdentityProvisioning supports create and reissue.
- Pending ASP.NET Identity user has no password, EmailConfirmed=false, TwoFactorEnabled=false, and no salon membership.
- 256-bit random activation token, 30-minute expiry, single-use; only SHA-256 hash stored. Re-issue rotates the prior token.
- API: POST /api/v1/auth/activate. Antiforgery required; IP rate-limited; generic invalid/expired/replayed response; password creation, email confirmation, token consumption and audit are atomic. No session issued.
- SES v2 uses AWS SDK for .NET v4 AWSSDK.SimpleEmailV2 4.1.0.2 and AWS credential provider chain. No permanent credentials in source.
- Database login atendee_identity_provisioner is NOINHERIT/NOSUPERUSER/NOBYPASSRLS and can SET only to atendee_identity_owner.
- Existing V41 first-Owner command remains the sole way to create the first salon and Owner membership, after email confirmation and MFA.

## V45 frontend details
- React 19 + TypeScript; static build uses esbuild and avoids Vite/Vitest dependencies.
- Reads token from URL fragment and removes fragment from visible URL/history before rendering.
- Calls GET /api/v1/auth/antiforgery, then POST /api/v1/auth/activate with X-CSRF-TOKEN and same-origin credentials.
- No localStorage/sessionStorage for token, password or antiforgery token; clears activation token/password from UI state after success.
- Does not issue or assume a signed-in session; explicitly indicates MFA setup remains.
- Same-origin deployment required. IIS URL Rewrite/ARR must proxy /api/* to the ASP.NET API; this server-level reverse proxy is not configured by the sample static web.config.
- Local development: set ATENDEE_API_PROXY_TARGET to local API origin, then npm run dev.

## Validation observed
Backend:
- GitHub Actions run 44279908188 passed for exact backend SHA b66e1e7ae3fc794891177dbeb89bfd7f3c2d7ef0.
- Build Release, domain, PostgreSQL 18.7 RLS/persistence, Identity/TOTP/activation, and API HTTP/antiforgery/no-session checks all passed.
Frontend local (Node.js 22.23.5):
- npm ci passed.
- npm test passed 2/2.
- npm run build passed (strict TypeScript + esbuild).
- npm audit reported 0 vulnerabilities.
- Static smoke GET / returned HTTP 200 and included CSP and Referrer-Policy headers.
- Local API proxy was not exercised against a running ASP.NET API.
- Frontend-inclusive GitHub Actions run is pending V45 push; do not claim CI pass until observed.

## Immediate next steps
1. Push V45 files and continuity update to this branch; verify exact SHA and files.
2. Verify frontend npm ci/audit/test/build and the full backend PostgreSQL 18.7 CI on the exact V45 SHA. Fix any failures.
3. Record the observed CI result in the next versioned continuity status and manifest.
4. After V45 CI passes, continue frontend onboarding (login and MFA setup) against existing backend contracts, preserving no-session-after-activation.
5. Configure SES sender identity/DNS, production access, IAM and temporary credentials only when external AWS/domain access is available and authorized. Never send a real email or deploy without the release prerequisites.

## External setup still pending
- SES sender domain/address verification, Easy DKIM, SPF/DMARC, SES production access and IAM credentials.
- Apply migration 011 and provision restricted DB login in target environment.
- IIS same-origin reverse proxy for /api/* and TLS/static hosting configuration.
- Login/MFA frontend pages; Platform Admin store/MFA/break-glass; WebAuthn/passkey.
- Non-Windows production Data Protection protector, distributed rate limiting and trusted proxy configuration.
- No production deployment authorized or performed.

## Continuity rule
After every completed increment, create a new versioned status file, update this index and manifest, commit directly to this branch, then verify exact SHA and files. Preserve prior versions. Record only tests/CI actually observed. No main changes, no deployment, no production data.
