# CONTINUE HERE — Atendee Beauty (auto-backup updated each step)

**Primary resume context:** read CLAUDE.md, this file, and the highest-numbered docs/continuity/CONTINUATION_STATUS_V*.md before changing code.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Active branch: security/csrf-webhook-antiforgery-v23; never modify main.
- Latest implementation commit with full CI verified: bd16f1d409c3a3571d7d6d5eab8ab39a328474ab.
- Latest CI: run 44282286929 passed for that exact SHA.

## Completed
- V41: offline first-Owner salon provisioning command. Requires existing identity with confirmed email, MFA enabled, no active lockout and no salon membership; atomically creates PendingVerification salon + active Owner membership. No HTTP endpoint.
- V42: approved separate protected operator command to create the initial operational identity and initiate activation.
- V43: selected Amazon SES API v2 as the default transactional email provider and documented activation design.
- V44: backend activation path implemented: dedicated provisioner role, migration 011, offline create/reissue CLI, SES v2 email sender, activation API, audit events, integration tests and runbook. V41 remains unchanged.
- V44 CI failure caused by missing authentication services in the isolated provisioner test provider; fixed in b66e1e7ae3fc794891177dbeb89bfd7f3c2d7ef0. Backend CI run 44279908188 passed for that exact SHA.
- V45: React/TypeScript activation page in web/atendee-portal. Frontend-inclusive CI run 44282286929 passed for exact commit bd16f1d409c3a3571d7d6d5eab8ab39a328474ab.

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
- Reads token from URL fragment and removes fragment from visible URL/history.
- Calls GET /api/v1/auth/antiforgery, then POST /api/v1/auth/activate with X-CSRF-TOKEN and same-origin credentials.
- No localStorage/sessionStorage for token, password or antiforgery token; clears activation token/password from UI state after success.
- Does not issue or assume a signed-in session; explicitly indicates MFA setup remains.
- Same-origin deployment required. IIS URL Rewrite/ARR must proxy /api/* to the ASP.NET API; this server-level reverse proxy is not configured by the sample static web.config.
- Local development: set ATENDEE_API_PROXY_TARGET to local API origin, then npm run dev.

## Validation observed
Backend and frontend:
- GitHub Actions run 44282286929 passed for exact V45 SHA bd16f1d409c3a3571d7d6d5eab8ab39a328474ab.
- Frontend npm ci, npm audit --audit-level=moderate, Node tests and strict TypeScript/esbuild build all passed.
- Backend locked restore, Release build, domain, PostgreSQL 18.7 persistence/RLS, Identity/TOTP/activation and API HTTP/antiforgery/no-session checks all passed.
- Local frontend tests passed 2/2; npm audit reported 0 vulnerabilities; static HTTP smoke returned 200 with CSP and Referrer-Policy headers.
- Local API proxy was not exercised against a running ASP.NET API.

## Immediate next steps
1. Inspect existing backend login and MFA endpoints/contracts, cookies, antiforgery, challenge and enrollment transitions.
2. Implement frontend login and MFA enrollment pages using those exact contracts; do not invent routes or session semantics.
3. Keep cookies same-origin; never store session, activation or antiforgery tokens in localStorage/sessionStorage.
4. Run npm ci/audit/test/build and the full PostgreSQL 18.7 workflow for each increment. Save a new continuity status and update this index/manifest after each completed step.
5. Configure SES sender identity/DNS, production access, IAM and temporary credentials only when external AWS/domain access is available and authorized. Never send a real email or deploy without the release prerequisites.

## External setup still pending
- SES sender domain/address verification, Easy DKIM, SPF/DMARC, SES production access and IAM credentials.
- Apply migration 011 and provision restricted DB login in target environment.
- IIS same-origin reverse proxy for /api/* and TLS/static hosting configuration.
- Frontend login and MFA setup pages; Platform Admin store/MFA/break-glass; WebAuthn/passkey.
- Non-Windows production Data Protection protector, distributed rate limiting and trusted proxy configuration.
- No production deployment authorized or performed.

## Continuity rule
After every completed increment, create a new versioned status file, update this index and manifest, commit directly to this branch, then verify exact SHA and files. Preserve prior versions. Record only tests/CI actually observed. No main changes, no deployment, no production data.
