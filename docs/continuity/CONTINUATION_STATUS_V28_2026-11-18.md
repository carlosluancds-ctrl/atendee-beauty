# Atendee Beauty - Continuity Status V28 (2026-11-18)

## Repository
- Branch: security/csrf-webhook-antiforgery-v23
- Base branch: wip/operational-identity-v1
- main remains untouched.
- No production deployment or signup enablement was performed.

## Security audit ledger
Migration 003 creates identity.SecurityAuditEvents in the isolated identity schema.

Allowlisted event types:
- operational_account.sessions_revoked
- operational_account.disabled
- operational_mfa.enrolled
- operational_mfa.totp_challenge_succeeded
- operational_mfa.recovery_code_redeemed

The writer stores only event ID/type, optional actor identity ID, target identity ID, UTC timestamp, and Activity trace ID. It does not accept request bodies, passwords, email addresses, IP addresses, TOTP values, authenticator secrets, or recovery-code values.

Account disablement/session revocation and their audit events share one identity database transaction. MFA enrollment and recovery-code redemption also share a transaction with their audit event. TOTP replay validation uses its own atomic transaction; successful TOTP audit is written in a following transaction before issuing the application cookie.

The application identity role has INSERT only on the audit table. UPDATE, DELETE, SELECT, and TRUNCATE are not granted. Database triggers also reject UPDATE, DELETE, and TRUNCATE. This protects against application-role tampering, not a privileged database owner/superuser.

No administrative audit query/export endpoint exists. Account disable/revoke methods remain internal service operations with no public endpoint.

## HTTP security coverage
- Antiforgery cookie/token and webhook-like unsafe-path checks.
- Generic unknown-user/wrong-password response.
- Secure application cookie and tenant context resolved from persisted membership.
- Security-stamp session revocation and account disablement.
- Durable audit assertions for account disable/revoke.
- Owner MFA enrollment, ten recovery codes, logout, one-time recovery-code use, TOTP challenge, and MFA audit assertions.
- Fresh TOTP step test validates replay protection rather than reusing the enrollment step.
- Role/MFA gate coverage across Owner, Manager, Receptionist, and Professional:
  - Owner and Manager without MFA cannot enter the application API.
  - Receptionist and Professional can establish password-only sessions under the current policy.
  - Promotion from Receptionist to Owner does not grant privileged access without MFA.
  - Owner demotion to Receptionist is reflected on the next request.
  - Membership deactivation invalidates an existing session on the next request.

## Validation
- Release build passes with zero warnings and zero errors.
- Domain checks: 15/15.
- The audit-ledger/MFA increment passed PostgreSQL 18.7 CI run 44210254600 on commit b028d154d87519fd1c48b7f66a81623451325f37.
- The expanded four-role matrix in this V28 increment requires its own CI confirmation after push.

## Remaining work
- Audit login success/failure, failed MFA, membership changes, and administrative actions.
- Build a reviewed tenant-role authorization matrix for future business/admin endpoints.
- Define tenant status access policy only after product confirmation; do not infer rules for PendingVerification, SetupRequired, Trial, Active, PastDue, Suspended, Closing, or Closed.
- Platform Admin identity, MFA and break-glass.
- WebAuthn/passkeys.
- Email provider, signup/invite/password reset.
- Distributed rate limiting and trusted proxy configuration.
- Non-Windows production Data Protection key protection.
- Restore drill and independent security review.

## Guardrails
- Do not modify main.
- Do not enable public signup.
- Do not deploy.
- Do not expose account disable/revoke endpoints before administrative authorization and MFA are ready.
- Do not decide tenant-status policy without the product owner.
