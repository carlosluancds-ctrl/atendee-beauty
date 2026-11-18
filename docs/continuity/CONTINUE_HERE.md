# CONTINUE HERE — Atendee Beauty (auto-backup updated each step)

**Primary resume context:** read CLAUDE.md, this file, and the highest-numbered docs/continuity/CONTINUATION_STATUS_V*.md before changing code.

## Repository
- https://github.com/carlosluancds-ctrl/atendee-beauty
- Active branch: security/csrf-webhook-antiforgery-v23; never modify main.
- Last implementation commit with full CI verified: bd16f1d409c3a3571d7d6d5eab8ab39a328474ab.
- V45 CI run 44282286929 passed; V46 continuity CI run 44282678565 passed.
- V47 login/MFA frontend increment is prepared in the temporary remote snapshot; commit and CI pending.

## Completed
- V41: offline first-Owner salon provisioning command. Requires existing identity with confirmed email, MFA enabled, no active lockout and no salon membership; atomically creates PendingVerification salon + active Owner membership. No HTTP endpoint.
- V42: approved separate protected operator command to create the initial operational identity and initiate activation.
- V43: selected Amazon SES API v2 as the default transactional email provider and documented activation design.
- V44: backend activation path implemented: dedicated provisioner role, migration 011, offline create/reissue CLI, SES v2 email sender, activation API, audit events, integration tests and runbook. V41 remains unchanged.
- V44 Identity test provider registration fix is in b66e1e7ae3fc794891177dbeb89bfd7f3c2d7ef0; CI run 44279908188 passed.
- V45: React/TypeScript activation page in web/atendee-portal. CI run 44282286929 passed for bd16f1d409c3a3571d7d6d5eab8ab39a328474ab.
- V46: continuity checkpoint; run 44282678565 passed for d96a50e676a34bfd9b595bc6ef1128b6826e8edd.
- V47: login, TOTP/recovery challenge, MFA setup, one-time recovery-code display and pending Owner state implemented locally; commit/CI pending.

## V44 backend details
- No public self-registration, public tenant creation, public account creation route, or public first-Owner endpoint.
- Offline command tools/Atendee.OperationalIdentityProvisioning supports create and reissue.
- Pending ASP.NET Identity user has no password, EmailConfirmed=false, TwoFactorEnabled=false, and no salon membership.
- 256-bit random activation token, 30-minute expiry, single-use; only SHA-256 hash stored. Re-issue rotates the prior token.
- API: POST /api/v1/auth/activate. Antiforgery required; IP rate-limited; generic invalid/expired/replayed response; password creation, email confirmation, token consumption and audit are atomic. No session issued.
- SES v2 uses AWS SDK for .NET v4 AWSSDK.SimpleEmailV2 4.1.0.2 and AWS credential provider chain. No permanent credentials in source.
- Database login atendee_identity_provisioner is NOINHERIT/NOSUPERUSER/NOBYPASSRLS and can SET only to atendee_identity_owner.
- Existing V41 first-Owner command remains the sole way to create the first salon and Owner membership, after email confirmation and MFA.

## V45/V47 frontend details
- React 19 + TypeScript; static build uses esbuild; no Vite/Vitest dependency tree.
- /activate reads token from URL fragment, removes fragment from visible URL/history, requests antiforgery token/cookie and posts activation.
- /login handles POST /api/v1/auth/login states authenticated, mfa_required and mfa_setup_required.
- MFA challenge uses POST /api/v1/auth/mfa/challenge for TOTP or recovery code.
- MFA enrollment uses /mfa/enrollment/start, /verify and /cancel with the short-lived MFA setup cookie.
- Recovery codes displayed once, held only in component memory, with explicit acknowledgement before leaving the screen.
- Pending first Owner completes MFA without an application session; V41 offline command still required to create salon membership.
- All unsafe API calls use X-CSRF-TOKEN and same-origin cookies. No localStorage/sessionStorage for auth or antiforgery material.
- Production same-origin deployment required. IIS URL Rewrite/ARR must proxy /api/* to ASP.NET API; not configured by sample static web.config.
- Local development: set ATENDEE_API_PROXY_TARGET to local API origin, then npm run dev.

## Validation observed
- V45 exact SHA bd16f1d409c3a3571d7d6d5eab8ab39a328474ab: full GitHub Actions run 44282286929 passed, including frontend audit/tests/build and backend PostgreSQL 18.7 integration.
- V46 exact SHA d96a50e676a34bfd9b595bc6ef1128b6826e8edd: full GitHub Actions run 44282678565 passed.
- V47 local npm ci passed, npm test 2/2, npm run build passed, npm audit 0 vulnerabilities.
- V47 static smoke: /login, /activate and /assets/app.js returned 200; CSP header present; compiled bundle includes login, MFA challenge and activation views.
- Local API proxy not tested against a running ASP.NET API.
- V47 commit and CI pending; do not claim CI pass until observed.

## Immediate next steps
1. Push V47 source and continuity files to this branch; verify exact SHA and all files.
2. Verify frontend npm ci/audit/test/build and full PostgreSQL 18.7 backend workflow on exact V47 SHA. Fix any failures.
3. Perform a browser-level review of activation, login, TOTP challenge, recovery-code challenge and pre-membership MFA enrollment. Avoid real email or production data.
4. Continue dashboard only after authentication/onboarding and backend contracts are verified.
5. Configure SES sender identity/DNS, production access, IAM and temporary credentials only when external AWS/domain access is available and authorized. Never deploy without explicit release gates.

## External setup still pending
- SES sender domain/address verification, Easy DKIM, SPF/DMARC, SES production access and IAM credentials.
- Apply migration 011 and provision restricted DB login in target environment.
- IIS same-origin reverse proxy for /api/* and TLS/static hosting configuration.
- Platform Admin store/MFA/break-glass; WebAuthn/passkey.
- Non-Windows production Data Protection protector, distributed rate limiting and trusted proxy configuration.
- No production deployment authorized or performed.

## Continuity rule
After every completed increment, create a new versioned status file, update this index and manifest, commit directly to this branch, then verify exact SHA and files. Preserve prior versions. Record only tests/CI actually observed. No main changes, no deployment, no production data.
