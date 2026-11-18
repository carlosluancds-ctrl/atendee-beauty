-- Database-level audit of tenant status changes. A trigger on atendee.tenants writes the
-- audit row in the same transaction as the status change, whatever code path performs it.
-- A status change targets a tenant, not an identity, so the ledger gains three nullable
-- columns (TargetTenantId, FromTenantStatus, ToTenantStatus). None is personal data.
-- A CHECK constraint requires exactly one kind of target per event:
--   tenant_status.changed -> tenant target + from/to status, no identity target
--   every other event     -> identity target, no tenant columns
-- The actor is NOT recorded (NULL): the database cannot trust a caller-supplied actor, and
-- no application path changes tenant status yet. The event type is deliberately NOT in the
-- application writer allowlist: only this trigger writes it.
-- Run as atendee_migrator after migrations 001-006.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL ROLE atendee_identity_owner;

ALTER TABLE identity."SecurityAuditEvents"
    ADD COLUMN "TargetTenantId" UUID NULL,
    ADD COLUMN "FromTenantStatus" SMALLINT NULL,
    ADD COLUMN "ToTenantStatus" SMALLINT NULL;

ALTER TABLE identity."SecurityAuditEvents"
    ALTER COLUMN "TargetIdentityUserId" DROP NOT NULL;

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
            'tenant_membership.deactivated',
            'tenant_status.changed'
        ));

ALTER TABLE identity."SecurityAuditEvents"
    ADD CONSTRAINT "CK_SecurityAuditEvents_TargetKind" CHECK
        (
            (
                "EventType" = 'tenant_status.changed'
                AND "TargetTenantId" IS NOT NULL
                AND "TargetIdentityUserId" IS NULL
                AND "FromTenantStatus" BETWEEN 0 AND 7
                AND "ToTenantStatus" BETWEEN 0 AND 7
                AND "FromTenantStatus" <> "ToTenantStatus"
            )
            OR
            (
                "EventType" <> 'tenant_status.changed'
                AND "TargetIdentityUserId" IS NOT NULL
                AND "TargetTenantId" IS NULL
                AND "FromTenantStatus" IS NULL
                AND "ToTenantStatus" IS NULL
            )
        );

CREATE INDEX "IX_SecurityAuditEvents_TargetTenant_OccurredAtUtc"
    ON identity."SecurityAuditEvents" ("TargetTenantId", "OccurredAtUtc" DESC)
    WHERE "TargetTenantId" IS NOT NULL;

CREATE FUNCTION identity.audit_tenant_status_change()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, identity
AS $function$
BEGIN
    INSERT INTO identity."SecurityAuditEvents"
        ("Id", "EventType", "ActorIdentityUserId", "TargetIdentityUserId", "OccurredAtUtc",
         "CorrelationId", "TargetTenantId", "FromTenantStatus", "ToTenantStatus")
    VALUES
        (gen_random_uuid(), 'tenant_status.changed', NULL, NULL, clock_timestamp(),
         NULL, NEW.id, OLD.status, NEW.status);
    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION identity.audit_tenant_status_change() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.audit_tenant_status_change() TO atendee_owner;

RESET ROLE;
SET LOCAL ROLE atendee_owner;

CREATE TRIGGER tr_tenants_status_audit
    AFTER UPDATE OF status ON atendee.tenants
    FOR EACH ROW
    WHEN (OLD.status IS DISTINCT FROM NEW.status)
    EXECUTE FUNCTION identity.audit_tenant_status_change();

RESET ROLE;
COMMIT;
