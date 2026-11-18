-- Derive TargetTenantId for identity-target audit events from persisted membership.
-- The audit writer and caller cannot choose the tenant: a BEFORE INSERT trigger
-- replaces any supplied value with the result of a narrow SECURITY DEFINER lookup.
-- If the target identity has no membership, TargetTenantId remains NULL.
-- Historical events are not backfilled: current membership is not proof of the
-- tenant at the time of an old event. This trigger resolves new inserts only.
-- Run as atendee_migrator after migrations 001-008.
\set ON_ERROR_STOP on
BEGIN;

-- Create a narrow resolver under the tenant owner. The identity audit trigger
-- may execute it, but neither the auth login nor the application login receives
-- direct SELECT access to atendee.tenant_users.
SET LOCAL ROLE atendee_owner;

GRANT USAGE ON SCHEMA atendee TO atendee_identity_owner;

CREATE FUNCTION atendee.resolve_audit_target_tenant()
RETURNS uuid
LANGUAGE SQL
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
    SELECT membership.tenant_id
    FROM atendee.tenant_users AS membership
    WHERE membership.identity_user_id =
        NULLIF(pg_catalog.current_setting('app.identity_user_id', true), '')::UUID;
$function$;

REVOKE ALL ON FUNCTION atendee.resolve_audit_target_tenant() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION atendee.resolve_audit_target_tenant() TO atendee_identity_owner;

RESET ROLE;
SET LOCAL ROLE atendee_identity_owner;

-- Identity-target events may now also carry a tenant derived by the trigger.
-- Tenant-status events remain tenant-only events with from/to status values.
ALTER TABLE identity."SecurityAuditEvents"
    DROP CONSTRAINT "CK_SecurityAuditEvents_TargetKind";

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
                AND "FromTenantStatus" IS NULL
                AND "ToTenantStatus" IS NULL
            )
        );

-- No FK to atendee.tenants is added here: tenant deletion and post-Closed
-- retention remain product decisions. The resolver guarantees the ID is backed
-- by a persisted membership when each new audit event is written.

CREATE FUNCTION identity.populate_security_audit_target_tenant()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, identity
AS $function$
DECLARE
    previous_identity_user_id text;
    resolved_tenant_id uuid;
BEGIN
    -- Tenant-status events have a tenant target already and no identity target.
    IF NEW."TargetIdentityUserId" IS NULL THEN
        RETURN NEW;
    END IF;

    previous_identity_user_id :=
        pg_catalog.current_setting('app.identity_user_id', true);

    PERFORM pg_catalog.set_config(
        'app.identity_user_id',
        NEW."TargetIdentityUserId"::text,
        true);

    SELECT atendee.resolve_audit_target_tenant()
        INTO resolved_tenant_id;

    -- Restore the caller's transaction-local context even if it was absent.
    PERFORM pg_catalog.set_config(
        'app.identity_user_id',
        COALESCE(previous_identity_user_id, ''),
        true);

    -- Never trust a caller-supplied TargetTenantId.
    NEW."TargetTenantId" := resolved_tenant_id;
    RETURN NEW;
EXCEPTION WHEN OTHERS THEN
    PERFORM pg_catalog.set_config(
        'app.identity_user_id',
        COALESCE(previous_identity_user_id, ''),
        true);
    RAISE;
END;
$function$;

REVOKE ALL ON FUNCTION identity.populate_security_audit_target_tenant() FROM PUBLIC;

CREATE TRIGGER "TR_SecurityAuditEvents_ResolveTargetTenant"
    BEFORE INSERT ON identity."SecurityAuditEvents"
    FOR EACH ROW
    EXECUTE FUNCTION identity.populate_security_audit_target_tenant();

RESET ROLE;
COMMIT;
