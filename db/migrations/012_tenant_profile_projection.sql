-- Extend the narrow authenticated membership projection with the current tenant's
-- public operational profile. The application login still receives no direct
-- SELECT on atendee.tenants or atendee.tenant_users.
\set ON_ERROR_STOP on
BEGIN;

SET LOCAL ROLE atendee_owner;

DROP FUNCTION atendee.resolve_tenant_membership();

CREATE FUNCTION atendee.resolve_tenant_membership()
RETURNS TABLE
(
    identity_user_id UUID,
    tenant_id UUID,
    display_name VARCHAR(120),
    role INTEGER,
    is_active BOOLEAN,
    tenant_status INTEGER,
    tenant_name VARCHAR(120),
    tenant_slug VARCHAR(60),
    tenant_time_zone_id VARCHAR(128)
)
LANGUAGE SQL
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
    SELECT
        membership.identity_user_id,
        membership.tenant_id,
        membership.display_name,
        membership.role::INTEGER,
        membership.is_active,
        tenant.status::INTEGER,
        tenant.name,
        tenant.slug,
        tenant.time_zone_id
    FROM atendee.tenant_users AS membership
    INNER JOIN atendee.tenants AS tenant
        ON tenant.id = membership.tenant_id
    WHERE membership.identity_user_id =
        NULLIF(current_setting('app.identity_user_id', true), '')::UUID;
$function$;

REVOKE ALL ON FUNCTION atendee.resolve_tenant_membership() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION atendee.resolve_tenant_membership() TO atendee_auth_login;

RESET ROLE;
COMMIT;
