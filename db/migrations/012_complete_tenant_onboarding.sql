-- Owner-only, idempotent transition from PendingVerification to SetupRequired.
-- The caller supplies the server-resolved actor and tenant; neither is accepted from HTTP input.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL ROLE atendee_owner;

CREATE FUNCTION atendee.complete_tenant_onboarding(
    p_tenant_id UUID,
    p_actor_id UUID
)
RETURNS TABLE (tenant_id UUID, status INTEGER)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
DECLARE
    current_status SMALLINT;
BEGIN
    IF p_tenant_id IS NULL OR p_actor_id IS NULL THEN
        RETURN;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM atendee.tenant_users AS membership
        WHERE membership.tenant_id = p_tenant_id
          AND membership.identity_user_id = p_actor_id
          AND membership.role = 1
          AND membership.is_active = true
    ) THEN
        RETURN;
    END IF;

    SELECT tenant.status
      INTO current_status
      FROM atendee.tenants AS tenant
     WHERE tenant.id = p_tenant_id
     FOR UPDATE;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    IF current_status = 0 THEN
        UPDATE atendee.tenants AS tenant
           SET status = 1
         WHERE tenant.id = p_tenant_id
        RETURNING tenant.id, tenant.status::INTEGER
             INTO tenant_id, status;
        RETURN NEXT;
        RETURN;
    END IF;

    IF current_status = 1 THEN
        tenant_id := p_tenant_id;
        status := current_status::INTEGER;
        RETURN NEXT;
    END IF;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.complete_tenant_onboarding(UUID, UUID) FROM PUBLIC;
GRANT USAGE ON SCHEMA atendee TO atendee_app_login;
GRANT EXECUTE ON FUNCTION atendee.complete_tenant_onboarding(UUID, UUID) TO atendee_app_login;

RESET ROLE;
COMMIT;
