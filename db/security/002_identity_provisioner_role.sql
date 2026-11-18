-- Run once as the target database DBA on an existing installation before
-- applying migration 011. New installations get this role from bootstrap
-- template db/security/001_roles_template.sql.
-- No password is set here. Provision credentials through the approved
-- database secret-management process.
DO $role$
DECLARE
    existing_role record;
BEGIN
    SELECT rolcanlogin, rolinherit, rolsuper, rolbypassrls,
           rolcreatedb, rolcreaterole, rolreplication
      INTO existing_role
      FROM pg_roles
     WHERE rolname = 'atendee_identity_provisioner';

    IF NOT FOUND THEN
        CREATE ROLE atendee_identity_provisioner
            LOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS;
    ELSIF NOT existing_role.rolcanlogin
       OR existing_role.rolinherit
       OR existing_role.rolsuper
       OR existing_role.rolbypassrls
       OR existing_role.rolcreatedb
       OR existing_role.rolcreaterole
       OR existing_role.rolreplication THEN
        RAISE EXCEPTION
            'atendee_identity_provisioner exists with unsafe role attributes; review manually.';
    END IF;
END
$role$;

GRANT atendee_identity_owner
    TO atendee_identity_provisioner
    WITH INHERIT FALSE, SET TRUE;

DO $connect$
BEGIN
    EXECUTE format(
        'GRANT CONNECT ON DATABASE %I TO atendee_identity_provisioner',
        current_database());
END
$connect$;