
-- Extends the allowlisted security audit event types with:
--   operational_auth.password_verified_mfa_pending  (password verified; MFA setup or
--                                                    challenge still required, no
--                                                    application session issued)
--   operational_mfa.enrollment_failed               (invalid authenticator code during
--                                                    MFA enrollment)
-- No new columns and no new data: events still carry only type, target identity,
-- optional actor identity, server UTC timestamp and trace ID.
-- Run as atendee_migrator after migrations 001-005.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL ROLE atendee_identity_owner;

ALTER TABLE identity."SecurityAuditEvents"
    DROP CONSTRAINT "CK_SecurityAuditEvents_EventType";

ALTER TABLE identity."SecurityAuditEvents"
    ADD CONSTRAINT "CK_SecurityAuditEvents_EventType" CHECK
        ("EventType" IN (
            'operational_account.disabled',
            'operational_account.sessions_revoked',
            'operational_mfa.enrolled',
            'operational_mfa.totp_challenge_succeeded',
            'operational_mfa.recovery_code_redeemed',
            'operational_mfa.challenge_failed',
            'operational_mfa.enrollment_failed',
            'operational_auth.login_succeeded',
            'operational_auth.login_failed',
            'operational_auth.password_verified_mfa_pending',
            'tenant_membership.created',
            'tenant_membership.role_changed',
            'tenant_membership.activated',
            'tenant_membership.deactivated'
        ));

COMMIT;
