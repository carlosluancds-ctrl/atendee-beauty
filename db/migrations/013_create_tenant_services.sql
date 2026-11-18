-- Tenant-scoped salon service catalogue with mandatory RLS and append-only audit.
-- Run as atendee_migrator after migrations 001-012.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL ROLE atendee_owner;

CREATE TABLE atendee.services
(
    tenant_id UUID NOT NULL,
    service_id UUID NOT NULL,
    name VARCHAR(120) NOT NULL,
    duration_minutes INTEGER NOT NULL,
    price_minor_units BIGINT NOT NULL,
    currency CHAR(3) NOT NULL,
    buffer_before_minutes INTEGER NOT NULL,
    buffer_after_minutes INTEGER NOT NULL,
    is_active BOOLEAN NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    CONSTRAINT pk_services PRIMARY KEY (tenant_id, service_id),
    CONSTRAINT ck_services_name_nonempty CHECK (length(btrim(name)) > 0),
    CONSTRAINT ck_services_duration CHECK (duration_minutes BETWEEN 1 AND 1440),
    CONSTRAINT ck_services_price CHECK (price_minor_units >= 0),
    CONSTRAINT ck_services_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_services_buffer_before CHECK (buffer_before_minutes BETWEEN 0 AND 1440),
    CONSTRAINT ck_services_buffer_after CHECK (buffer_after_minutes BETWEEN 0 AND 1440),
    CONSTRAINT ck_services_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX ix_services_tenant_active_name
    ON atendee.services (tenant_id, is_active, name, service_id);

ALTER TABLE atendee.services ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.services FORCE ROW LEVEL SECURITY;

CREATE POLICY services_runtime_isolation
ON atendee.services
FOR ALL
TO atendee_runtime
USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID)
WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID);

CREATE TABLE atendee.tenant_audit_events
(
    tenant_id UUID NOT NULL,
    event_id UUID NOT NULL,
    actor_identity_user_id UUID NOT NULL,
    event_type VARCHAR(80) NOT NULL,
    resource_id UUID NOT NULL,
    occurred_at_utc TIMESTAMPTZ NOT NULL,
    CONSTRAINT pk_tenant_audit_events PRIMARY KEY (tenant_id, event_id),
    CONSTRAINT ck_tenant_audit_event_type CHECK (
        event_type IN ('service.created', 'service.updated', 'service.archived')
    )
);

CREATE INDEX ix_tenant_audit_events_tenant_resource_time
    ON atendee.tenant_audit_events (tenant_id, resource_id, occurred_at_utc DESC);

ALTER TABLE atendee.tenant_audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.tenant_audit_events FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_audit_events_runtime_read
ON atendee.tenant_audit_events
FOR SELECT
TO atendee_runtime
USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID);

CREATE POLICY tenant_audit_events_owner_access
ON atendee.tenant_audit_events
FOR ALL
TO atendee_owner
USING (true)
WITH CHECK (true);

GRANT SELECT ON atendee.tenant_audit_events TO atendee_runtime;
REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON atendee.tenant_audit_events FROM atendee_runtime, PUBLIC;

CREATE FUNCTION atendee.audit_service_change()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
DECLARE
    actor_text text;
    audit_type text;
BEGIN
    actor_text := NULLIF(current_setting('app.actor_id', true), '');
    IF actor_text IS NULL THEN
        RAISE EXCEPTION 'Trusted tenant actor context is required for service mutations';
    END IF;

    IF TG_OP = 'INSERT' THEN
        audit_type := 'service.created';
    ELSIF OLD.is_active AND NOT NEW.is_active THEN
        audit_type := 'service.archived';
    ELSE
        audit_type := 'service.updated';
    END IF;

    INSERT INTO atendee.tenant_audit_events
        (tenant_id, event_id, actor_identity_user_id, event_type, resource_id, occurred_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), actor_text::uuid, audit_type, NEW.service_id, clock_timestamp());

    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.audit_service_change() FROM PUBLIC;

CREATE TRIGGER tr_services_audit
    AFTER INSERT OR UPDATE ON atendee.services
    FOR EACH ROW
    EXECUTE FUNCTION atendee.audit_service_change();

GRANT SELECT, INSERT, UPDATE ON atendee.services TO atendee_runtime;
REVOKE DELETE, TRUNCATE ON atendee.services FROM atendee_runtime, PUBLIC;

RESET ROLE;
COMMIT;
