-- TEMPLATE ONLY. Run in the target database as a dedicated DBA.
-- No passwords, keys or credentials belong in this file or source control.
-- Review pg_hba.conf and provision passwords from a secret manager afterwards.

DO $preflight$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_roles
        WHERE rolname IN ('atendee_owner', 'atendee_runtime', 'atendee_app_login', 'atendee_migrator')
    ) THEN
        RAISE EXCEPTION 'Atendee roles already exist. Review the database manually; do not rerun blindly.';
    END IF;
END
$preflight$;

CREATE ROLE atendee_owner
    NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS;
CREATE ROLE atendee_runtime
    NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS;
CREATE ROLE atendee_app_login
    LOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS;
CREATE ROLE atendee_migrator
    LOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS;

GRANT atendee_runtime TO atendee_app_login WITH INHERIT FALSE, SET TRUE;
GRANT atendee_owner TO atendee_migrator WITH INHERIT FALSE, SET TRUE;

DO $grant_connect$
BEGIN
    EXECUTE format(
        'GRANT CONNECT ON DATABASE %I TO atendee_app_login, atendee_migrator',
        current_database());
END
$grant_connect$;

-- Create the schema as the database administrator and assign ownership
-- to the dedicated no-login owner role. Do not grant database CREATE to it.
CREATE SCHEMA atendee AUTHORIZATION atendee_owner;

REVOKE ALL ON SCHEMA atendee FROM PUBLIC;
GRANT USAGE ON SCHEMA atendee TO atendee_runtime;

ALTER DEFAULT PRIVILEGES FOR ROLE atendee_owner IN SCHEMA atendee
    REVOKE ALL ON TABLES FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE atendee_owner IN SCHEMA atendee
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO atendee_runtime;

-- atendee_app_login has no direct table privileges. The API must SET LOCAL ROLE
-- atendee_runtime inside each tenant transaction before issuing tenant queries.
