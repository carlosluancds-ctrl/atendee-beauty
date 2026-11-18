-- Database-level audit of tenant membership changes (created, role changed,
-- activated, deactivated). A trigger guarantees the audit row is written in the
-- same transaction as the membership change, whatever code path performs it.
-- The actor is NOT recorded (NULL): the database cannot trust a caller-supplied
-- actor, and no application path changes memberships yet.
-- Run as atendee_migrator after migrations 001-004.
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
            'operational_auth.login_failed',
            'tenant_membership.created',
            'tenant_membership.role_changed',
            'tenant_membership.activated',
            'tenant_membership.deactivated'
        ));

CREATE FUNCTION identity.audit_tenant_membership_change()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, identity
AS $function$
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO identity."SecurityAuditEvents"
            ("Id", "EventType", "ActorIdentityUserId", "TargetIdentityUserId", "OccurredAtUtc", "CorrelationId")
        VALUES
            (gen_random_uuid(), 'tenant_membership.created', NULL, NEW.identity_user_id, clock_timestamp(), NULL);
        RETURN NEW;
    END IF;

    IF NEW.role IS DISTINCT FROM OLD.role THEN
        INSERT INTO identity."SecurityAuditEvents"
            ("Id", "EventType", "ActorIdentityUserId", "TargetIdentityUserId", "OccurredAtUtc", "CorrelationId")
        VALUES
            (gen_random_uuid(), 'tenant_membership.role_changed', NULL, NEW.identity_user_id, clock_timestamp(), NULL);
    END IF;

    IF NEW.is_active IS DISTINCT FROM OLD.is_active THEN
        INSERT INTO identity."SecurityAuditEvents"
            ("Id", "EventType", "ActorIdentityUserId", "TargetIdentityUserId", "OccurredAtUtc", "CorrelationId")
        VALUES
            (gen_random_uuid(),
             CASE WHEN NEW.is_active THEN 'tenant_membership.activated' ELSE 'tenant_membership.deactivated' END,
             NULL, NEW.identity_user_id, clock_timestamp(), NULL);
    END IF;

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION identity.audit_tenant_membership_change() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.audit_tenant_membership_change() TO atendee_owner;

RESET ROLE;
SET LOCAL ROLE atendee_owner;

CREATE TRIGGER tr_tenant_users_audit
    AFTER INSERT OR UPDATE OF role, is_active ON atendee.tenant_users
    FOR EACH ROW EXECUTE FUNCTION identity.audit_tenant_membership_change();

RESET ROLE;
COMMIT;