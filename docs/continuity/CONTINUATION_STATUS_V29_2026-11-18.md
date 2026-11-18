# Atendee Beauty - Continuity Status V29 (2026-11-18)

## Repository
- Branch: security/csrf-webhook-antiforgery-v23
- Base branch: wip/operational-identity-v1
- Previous verified HEAD: c763e1fce5564a3a973cdd9cc411ef7392b3fd2c (CI run 44210975463, success)
- main remains untouched. No deployment, no public signup.

## Increment: authentication audit coverage (Priority A, part 1)
New allowlisted audit event types (migration 004 extends the CHECK constraint; migration 003 is not edited):
- operational_auth.login_succeeded - password-only session cookie issued (Receptionist/Professional). Actor = target = identity. Written BEFORE the cookie is issued, so an audit failure yields an error instead of a silent success.
- operational_auth.login_failed - known identity with wrong password, or correct password but no active/permitted membership. Actor is null (unauthenticated), target = identity.
- operational_mfa.challenge_failed - failed TOTP or recovery-code challenge. Recovery-code failure is written in the same transaction as the AccessFailed increment.

Decisions and documented limitations:
- Unknown emails are NOT audited: there is no trusted target identity and the email must never be persisted.
- Attempts against an already locked-out account are NOT audited individually (the lockout is the recorded state). This bounds audit-table growth under credential stuffing together with the existing auth-login / auth-mfa rate limiters.
- Login-failure audit is a separate write after Identity's own AccessFailed update (not one transaction). Password-verified-but-MFA-pending logins are not yet audited as a distinct event.
- MFA enrollment code failures are not audited yet.
- Tenant ID is not stored in audit events (no transactional trusted source in this flow yet).
- No PII/secret is persisted: only event ID/type, actor ID, target ID, server UTC timestamp, trace ID.

## Tests (tests/Atendee.Api.HttpChecks)
- Receptionist flow now asserts exactly: 1 login_failed, 2 login_succeeded, 1 sessions_revoked, 1 disabled; no challenge_failed.
- Login attempt against the disabled (locked-out) account does not grow the ledger.
- Replay of a redeemed recovery code records exactly one operational_mfa.challenge_failed.
- Release build: 0 warnings / 0 errors locally. CI result is recorded below once observed.

## CI
- Pending for this commit (see CI result section appended after observation).

## Remaining work
- Audit membership role changes / activation / deactivation (needs trusted writer path).
- MFA enrollment failures, password-verified-pending-MFA event.
- Endpoint authorization matrix (Priority B).
- Everything listed as remaining in V28 stays open; tenant-status access policy still requires the product owner's decision.