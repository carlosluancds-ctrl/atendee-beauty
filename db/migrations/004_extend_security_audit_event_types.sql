-- Extends the allowlisted security audit event types with authentication
-- outcomes (login success/failure, failed MFA challenge).
-- Run as atendee_migrator after migrations 001, 002 and 003.
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
            'operational_auth.login_succeeded',
            'operational_auth.login_failed'
        ));

COMMIT;