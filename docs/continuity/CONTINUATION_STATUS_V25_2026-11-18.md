# Atendee Beauty — Continuation Status V25
Date: 2026-11-18

## Repository state
- Repository: `carlosluancds-ctrl/atendee-beauty`
- Working branch: `security/csrf-webhook-antiforgery-v23`
- Latest commit: `cabeaa4dfc270eb835760a8046f6c1a5a53c8cb2` — Configure HTTP test host identity services
- Previous commits: `916c936` (HTTP test content root), `d00f534` (HTTP checks and session revocation), `5d859e9` (antiforgery webhook boundary)
- `main` was not changed. No merge or deployment was performed.
- Working tree was clean after publishing the latest commit.

## CI result
GitHub Actions workflow: PostgreSQL 18 persistence integration
- Run: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44206734333
- Result: SUCCESS
- Head SHA: `cabeaa4dfc270eb835760a8046f6c1a5a53c8cb2`
- Duration: 55 seconds

All workflow stages passed:
1. Locked dependency restore
2. Release build
3. Domain checks (15/15)
4. Isolated PostgreSQL 18.7 startup and restricted database-role provisioning
5. EF Core and PostgreSQL RLS integration checks
6. Identity, TOTP and tenant-context integration checks
7. API HTTP authentication and session-security checks
8. Disposable credential cleanup and PostgreSQL container removal

The HTTP integration run reported:
- Antiforgery bootstrap endpoint and secure antiforgery cookie attributes.
- Request token issuance.
- Unsafe webhook-like path prefixes do not bypass antiforgery.
- Unknown account and incorrect password produce the same generic login response.
- Valid login creates an authenticated session with secure application-cookie attributes.
- Tenant ID and role are resolved from persisted server-side membership.
- Rotating the security stamp invalidates the existing cookie on the next request.
- Session-only revocation permits a fresh login.
- Account disablement invalidates an already-issued cookie and blocks subsequent login.

The test harness required two corrections discovered by CI: explicitly set the API content root by locating `Atendee.sln`, and set connection strings in process environment variables before minimal hosting starts. Both corrections are included in the published branch.

## V24 security changes
- Identity security-stamp validation is configured with `ValidationInterval = TimeSpan.Zero`; revocation/disablement therefore takes effect on the next authenticated request. This adds an Identity-store validation on each authenticated request and must be load/performance-tested before production.
- Added an operational account security service for session revocation and account disablement.
- No administrative HTTP endpoint exposes these operations yet; durable audit logging and authorization must exist first.
- MFA enrollment verification now participates in Identity lockout handling and rejects stale setup sessions after MFA is enabled.
- HTTP integration tests are part of the PostgreSQL workflow.

## Remaining work — not production-ready
- Durable security/audit event pipeline and audited admin actions.
- Complete HTTP coverage for logout, MFA enrollment/challenge/recovery, membership deactivation, and authorization boundaries.
- Explicit tenant-status authorization matrix for PendingVerification, SetupRequired, Trial, Active, PastDue, Suspended, Closing and Closed. This is a product/security decision and must not be guessed.
- Distributed rate limiting and trusted-proxy configuration.
- Production Data Protection key management, especially non-Windows certificate/managed-key protection. The Development-only HTTP test emitted the expected warning that its temporary test key ring has no XML encryptor; do not treat that test setup as production key protection.
- Platform Admin MFA and break-glass process.
- Email provider, signup/invitation/password-reset flows.
- WebAuthn/passkeys and reviewed schema migration.
- Independent security review and database restore drill.

## Guardrails
- Keep work on the feature/security branch; do not modify `main`.
- Do not enable public signup.
- Do not deploy or represent this branch as production-ready.
- Before exposing disable/revoke operations through admin APIs, add role/resource authorization and durable audit records.
