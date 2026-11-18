# Atendee Beauty — Continuation Status V26
Date: 2026-11-18

## Repository state
- Repository: `carlosluancds-ctrl/atendee-beauty`
- Working branch: `security/csrf-webhook-antiforgery-v23`
- Code/test head: `4b63a568518fe51b2fe7efff232e3cfa5de3cc81` — Test live tenant membership authorization changes
- Previous security fix: `7015d1164abfdb8ee5059fba674f9e378f4fde87` — Preserve MFA claims during security stamp refresh
- Working tree was clean after the code/test push.
- `main` remains untouched. No merge or deployment was performed.

## Latest CI result
- Workflow: PostgreSQL 18 persistence integration
- Run: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44208596793
- Conclusion: SUCCESS
- Head SHA: `4b63a568518fe51b2fe7efff232e3cfa5de3cc81`
- Duration: 45 seconds

Passed stages:
1. Locked dependency restore
2. Release build
3. Domain checks: 15/15
4. Isolated PostgreSQL 18.7 startup and restricted role provisioning
5. EF Core and PostgreSQL RLS checks
6. Identity/TOTP/tenant-context checks: 23/23
7. API HTTP authentication, antiforgery, cookie, session, logout, MFA and live membership checks
8. Credential cleanup and disposable PostgreSQL container removal

## New HTTP coverage
The HTTP integration suite now checks:
- Antiforgery bootstrap, secure antiforgery cookie attributes and token issuance.
- Unsafe requests under webhook-like path prefixes do not bypass antiforgery.
- Unknown identity and incorrect password return identical generic failures.
- Application authentication cookie security attributes.
- Server-side tenant and membership role resolution.
- Security-stamp rotation invalidates existing sessions on the next request.
- Account disablement invalidates current sessions and blocks new login.
- Owner without MFA receives only the short-lived setup session.
- TOTP enrollment uses the configured operational provider (`AtendeeTOTP`).
- Successful enrollment issues ten recovery codes and an authenticated Owner session.
- Logout clears the application session.
- MFA-enabled Owner login receives only a two-factor challenge.
- Recovery code can be redeemed once and cannot be replayed.
- MFA authentication marker survives immediate security-stamp principal refresh.
- Promotion of a password-only Receptionist session to Owner is rejected.
- Deactivation of an active tenant membership invalidates its existing session on the next request.

## Security fix discovered by the new tests
With `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`, Identity refreshes the principal on every authenticated request. The default refresh rebuilt the principal from persisted user claims and dropped the session-scoped `amr=pwd` / `amr=mfa` markers. That caused an MFA-authenticated Owner to lose access on the next request.

The API now uses `SecurityStampValidatorOptions.OnRefreshingPrincipal` to preserve only the allowlisted, already-signed `amr` values `pwd` and `mfa` from the validated current principal. The HTTP test explicitly verifies Owner access survives the next request. Security-stamp validation still runs on every request, preserving immediate revocation semantics.

The test harness also now generates TOTP codes using the actual configured provider name (`users.Options.Tokens.AuthenticatorTokenProvider`) rather than assuming Identity's default provider. Successful test output does not print recovery-code response bodies.

## CI diagnostic note
An earlier failed diagnostic run printed recovery codes belonging only to a synthetic test Owner in the disposable CI database while the harness included a response body in its assertion message. The test was corrected so response bodies are emitted only on failure; the successful run above logs no recovery codes. No production identity, account or recovery code was involved.

## Remaining work — not production-ready
- Durable security/audit event pipeline and audited admin actions.
- Role/resource authorization matrix and administrative account operations.
- Explicit tenant-status authorization matrix for PendingVerification, SetupRequired, Trial, Active, PastDue, Suspended, Closing and Closed. This remains a product/security decision; do not guess.
- Distributed rate limiting and trusted-proxy configuration.
- Production Data Protection key management, especially non-Windows certificate/managed-key protection.
- Platform Admin MFA and break-glass process.
- Email provider, signup/invitation/password-reset flows.
- WebAuthn/passkeys and reviewed schema migration.
- Independent security review and database restore drill.
- Performance/load testing for per-request Identity security-stamp validation.

## Guardrails
- Continue on the security feature branch; do not modify `main`.
- Do not enable public signup.
- Do not deploy or represent this branch as production-ready.
- Do not expose disable/revoke operations through admin APIs until role/resource authorization and durable audit records exist.
