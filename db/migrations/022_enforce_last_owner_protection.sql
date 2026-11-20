-- Database-level Last-Owner protection. A deferred constraint trigger prevents
-- removing the last active, MFA-enrolled Owner from any tenant. The trigger
-- covers three mutation paths: tenant_users role/is_active changes,
-- identity.AspNetUsers TwoFactorEnabled changes, and direct DELETE from tenant_users.
-- The actor is NOT recorded: the database cannot trust a caller-supplied actor.
-- Run as atendee_migrator after migrations 001-021.
\set ON_ERROR_STOP on
BEGIN;

SET LOCAL ROLE atendee_identity_owner;

-- Grant usage for the SECURITY DEFINER function to access atendee.tenant_users
GRANT USAGE ON SCHEMA atendee TO atendee_identity_owner;
GRANT SELECT ON atendee.tenant_users TO atendee_identity_owner;

RESET ROLE;
SET LOCAL ROLE atendee_identity_owner;

-- Lock function serializes concurrent membership/MFA changes per tenant.
CREATE FUNCTION identity.lock_tenant_last_owner()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, identity, atendee
AS $function$
DECLARE
    tenant_ids UUID[] := ARRAY[]::UUID[];
    tenant_key UUID;
BEGIN
    IF TG_TABLE_SCHEMA = 'atendee' AND TG_TABLE_NAME = 'tenant_users' THEN
        IF TG_OP <> 'INSERT' THEN
            tenant_ids := array_append(tenant_ids, OLD.tenant_id);
        END IF;
        IF TG_OP <> 'DELETE' THEN
            tenant_ids := array_append(tenant_ids, NEW.tenant_id);
        END IF;
    ELSIF TG_TABLE_SCHEMA = 'identity' AND TG_TABLE_NAME = 'AspNetUsers' THEN
        SELECT array_agg(membership.tenant_id)
          INTO tenant_ids
          FROM atendee.tenant_users AS membership
         WHERE membership.identity_user_id = NEW."Id";
    END IF;

    FOR tenant_key IN
        SELECT DISTINCT item
          FROM unnest(tenant_ids) AS item
         WHERE item IS NOT NULL
         ORDER BY item
    LOOP
        PERFORM pg_catalog.pg_advisory_xact_lock(
            pg_catalog.hashtextextended(tenant_key::text, 73190422));
    END LOOP;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$function$;

-- Assertion function: every tenant must retain at least one active Owner with MFA enabled.
-- Also ensures no active Owner exists without MFA.
CREATE FUNCTION identity.assert_tenant_has_active_mfa_owner()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, identity, atendee
AS $function$
DECLARE
    tenant_ids UUID[] := ARRAY[]::UUID[];
    tenant_key UUID;
    identity_key UUID;
BEGIN
    IF TG_TABLE_SCHEMA = 'atendee' AND TG_TABLE_NAME = 'tenant_users' THEN
        IF TG_OP <> 'INSERT' THEN
            tenant_ids := array_append(tenant_ids, OLD.tenant_id);
        END IF
        IF TG_OP <> 'DELETE' THEN
            tenant_ids := array_append(tenant_ids, NEW.tenant_id);
        END IF;
    ELSIF TG_TABLE_SCHEMA = 'identity' AND TG_TABLE_NAME = 'AspNetUsers' THEN
        identity_key := NEW."Id";
        SELECT array_agg(membership.tenant_id)
          INTO tenant_ids
          FROM atendee.tenant_users AS membership
         WHERE membership.identity_user_id = identity_key;
    END IF;

    FOR tenant_key IN
        SELECT DISTINCT item
          FROM unnest(tenant_ids) AS item
         WHERE item IS NOT NULL
         ORDER BY item
    LOOP
        -- Check: at least one active Owner with MFA enabled
        IF NOT EXISTS
        (
            SELECT 1
              FROM atendee.tenant_users AS membership
              INNER JOIN identity."AspNetUsers" AS identity_user
                ON identity_user."Id" = membership.identity_user_id
             WHERE membership.tenant_id = tenant_key
               AND membership.role = 1
               AND membership.is_active
               AND identity_user."TwoFactorEnabled"
        )
        OR EXISTS
        (
            -- Check: no active Owner without MFA
            SELECT 1
              FROM atendee.tenant_users AS membership
              INNER JOIN identity."AspNetUsers" AS identity_user
                ON identity_user."Id" = membership.identity_user_id
             WHERE membership.tenant_id = tenant_key
               AND membership.role = 1
               AND membership.is_active
               AND NOT identity_user."TwoFactorEnabled"
        ) THEN
            RAISE EXCEPTION
                'Salon must retain an active MFA-enabled Owner, and no active Owner may lack MFA.'
                USING ERRCODE = '23514',
                      CONSTRAINT = 'ck_tenant_active_owner_mfa';
        END IF;
    END LOOP;

    RETURN NULL;
END;
$function$;

REVOKE ALL ON FUNCTION identity.lock_tenant_last_owner() FROM PUBLIC;
REVOKE ALL ON FUNCTION identity.assert_tenant_has_active_mfa_owner() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION identity.lock_tenant_last_owner() TO atendee_owner;
GRANT EXECUTE ON FUNCTION identity.assert_tenant_has_active_mfa_owner() TO atendee_owner;

RESET ROLE;
SET LOCAL ROLE atendee_owner;

-- Lock triggers: serialize all relevant mutations per tenant
CREATE TRIGGER tr_tenant_users_last_owner_lock
    BEFORE INSERT OR UPDATE OR DELETE ON atendee.tenant_users
    FOR EACH ROW EXECUTE FUNCTION identity.lock_tenant_last_owner();

CREATE TRIGGER tr_tenants_last_owner_lock
    BEFORE INSERT ON atendee.tenants
    FOR EACH ROW EXECUTE FUNCTION identity.lock_tenant_last_owner();

-- Deferred constraint triggers: run at transaction commit, fail-closed
CREATE CONSTRAINT TRIGGER ct_tenant_users_active_mfa_owner
    AFTER INSERT OR UPDATE OR DELETE ON atendee.tenant_users
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION identity.assert_tenant_has_active_mfa_owner();

CREATE CONSTRAINT TRIGGER ct_tenants_active_mfa_owner
    AFTER INSERT ON atendee.tenants
    DEFERRED INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION identity.assert_tenant_has_active_mfa_owner();

RESET ROLE;
SET LOCAL ROLE atendee_identity_owner;

-- Lock trigger on identity.AspNetUsers for TwoFactorEnabled changes
CREATE TRIGGER tr_identity_mfa_last_owner_lock
    BEFORE UPDATE OF "TwoFactorEnabled" ON identity."AspNetUsers"
    FOR EACH ROW
    WHEN (OLD."TwoFactorEnabled" IS DISTINCT FROM NEW."TwoFactorEnabled")
    EXECUTE FUNCTION identity.lock_tenant_last_owner();

-- Deferred constraint trigger on identity.AspNetUsers for TwoFactorEnabled changes
CREATE CONSTRAINT TRIGGER ct_identity_mfa_active_owner
    AFTER UPDATE ON identity."AspNetUsers"
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
    WHEN (OLD."TwoFactorEnabled" IS DISTINCT FROM NEW."TwoFactorEnabled")
    EXECUTE FUNCTION identity.assert_tenant_has_active_mfa_owner();

COMMIT;