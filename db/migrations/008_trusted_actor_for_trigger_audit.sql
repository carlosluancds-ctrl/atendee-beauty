-- Trusted actor propagation for trigger-written audit events (ADMIN_ACTIONS_DESIGN_V1, step 1).
--
-- The membership and tenant-status triggers used to record a NULL actor. They now read the
-- transaction-local setting app.actor_id, which ONLY server code may set from the authenticated
-- principal (Atendee.Infrastructure TrustedActorPropagation, using set_config(..., is_local => true)).
--
-- Rules enforced by identity.trusted_audit_actor():
--   * setting absent or empty        -> actor NULL (maintenance and migration paths stay valid);
--   * setting present but malformed  -> the change FAILS (22023); it is never stored as NULL;
--   * setting is the nil UUID        -> the change FAILS (22023);
--   * setting names no identity      -> the change FAILS (23503) through the existing actor FK.
--
-- HONEST LIMIT: PostgreSQL cannot authenticate who set a custom setting. Any session able to run
-- SQL as a role that can UPDATE the audited tables can also set app.actor_id. The guarantee is
-- therefore "the application never takes the actor from client input", enforced in code and
-- tested; it is NOT a defence against a compromised application role or a DB owner/superuser.
-- Run as atendee_migrator after migrations 001-007.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL ROLE atendee_identity_owner;

CREATE FUNCTION identity.trusted_audit_actor()
RETURNS uuid
LANGUAGE plpgsql
STABLE
SET search_path = pg_catalog, identity
AS $function$
DECLARE
    raw_actor text;
    actor uuid;
BEGIN
    raw_actor := NULLIF(pg_catalog.current_setting('app.actor_id', true), '');
    IF raw_actor IS NULL THEN
        RETURN NULL;
    END IF;

    BEGIN
        actor := raw_actor::uuid;
    EXCEPTION WHEN invalid_text_representation THEN
        -- Generic message on purpose: do not echo the rejected value.
        RAISE EXCEPTION 'Invalid trusted audit actor.' USING ERRCODE = '22023';
    END;

    IF actor = '00000000-0000-0000-0000-000000000000'::uuid THEN
        RAISE EXCEPTION 'Invalid trusted audit actor.' USING ERRCODE = '22023';
    END IF;

    RETURN actor;
END;
$function$;

REVOKE ALL ON FUNCTION identity.trusted_audit_actor() FROM PUBLIC;

-- CREATE OR REPLACE keeps the owner, the EXECUTE grant to atendee_owner and the existing triggers.
CREATE OR REPLACE FUNCTION identity.audit_tenant_membership_change()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, identity
AS $function$
DECLARE
    trusted_actor uuid := identity.trusted_audit_actor();
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO identity."SecurityAuditEvents"
            ("Id", "EventType", "ActorIdentityUserId", "TargetIdentityUserId", "OccurredAtUtc", "CorrelationId")
        VALUES
            (gen_random_uuid(), 'tenant_membership.created', trusted_actor, NEW.identity_user_id, clock_timestamp(), NULL);
        RETURN NEW;
    END IF;

    IF NEW.role IS DISTINCT FROM OLD.role THEN
        INSERT INTO identity."SecurityAuditEvents"
            ("Id", "EventType", "ActorIdentityUserId", "TargetIdentityUserId", "OccurredAtUtc", "CorrelationId")
        VALUES
            (gen_random_uuid(), 'tenant_membership.role_changed', trusted_actor, NEW.identity_user_id, clock_timestamp(), NULL);
    END IF;

    IF NEW.is_active IS DISTINCT FROM OLD.is_active THEN
        INSERT INTO identity."SecurityAuditEvents"
            ("Id", "EventType", "ActorIdentityUserId", "TargetIdentityUserId", "OccurredAtUtc", "CorrelationId")
        VALUES
            (gen_random_uuid(),
             CASE WHEN NEW.is_active THEN 'tenant_membership.activated' ELSE 'tenant_membership.deactivated' END,
             trusted_actor, NEW.identity_user_id, clock_timestamp(), NULL);
    END IF;

    RETURN NEW;
END;
$function$;

CREATE OR REPLACE FUNCTION identity.audit_tenant_status_change()
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
        (gen_random_uuid(), 'tenant_status.changed', identity.trusted_audit_actor(), NULL, clock_timestamp(),
         NULL, NEW.id, OLD.status, NEW.status);
    RETURN NEW;
END;
$function$;

RESET ROLE;
COMMIT;
