-- Professional profiles and service assignments, isolated per tenant.
-- Run as atendee_migrator after migrations 001-013.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL ROLE atendee_owner;

ALTER TABLE atendee.tenant_audit_events
    ADD COLUMN related_resource_id UUID NULL;

ALTER TABLE atendee.tenant_audit_events
    DROP CONSTRAINT ck_tenant_audit_event_type;

ALTER TABLE atendee.tenant_audit_events
    ADD CONSTRAINT ck_tenant_audit_event_type CHECK (
        event_type IN (
            'service.created', 'service.updated', 'service.archived',
            'professional.created', 'professional.updated',
            'professional_service.assigned', 'professional_service.unassigned'
        )
    );

CREATE TABLE atendee.professionals
(
    tenant_id UUID NOT NULL,
    professional_id UUID NOT NULL,
    identity_user_id UUID NOT NULL,
    is_active BOOLEAN NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL,
    CONSTRAINT pk_professionals PRIMARY KEY (tenant_id, professional_id),
    CONSTRAINT uq_professionals_identity UNIQUE (tenant_id, identity_user_id),
    CONSTRAINT fk_professionals_membership
        FOREIGN KEY (tenant_id, identity_user_id)
        REFERENCES atendee.tenant_users (tenant_id, identity_user_id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_professionals_timestamps CHECK (updated_at_utc >= created_at_utc)
);

CREATE INDEX ix_professionals_tenant_active
    ON atendee.professionals (tenant_id, is_active, professional_id);

ALTER TABLE atendee.professionals ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.professionals FORCE ROW LEVEL SECURITY;

CREATE POLICY professionals_runtime_isolation
ON atendee.professionals
FOR ALL
TO atendee_runtime
USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID)
WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID);

CREATE FUNCTION atendee.validate_professional_membership()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
DECLARE
    member_role smallint;
    member_active boolean;
BEGIN
    SELECT role, is_active INTO member_role, member_active
    FROM atendee.tenant_users
    WHERE tenant_id = NEW.tenant_id
      AND identity_user_id = NEW.identity_user_id
    FOR KEY SHARE;

    IF member_role IS DISTINCT FROM 4 OR member_active IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'An active Professional membership is required';
    END IF;
    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.validate_professional_membership() FROM PUBLIC;

CREATE TRIGGER tr_professionals_validate_membership
    BEFORE INSERT OR UPDATE OF tenant_id, identity_user_id ON atendee.professionals
    FOR EACH ROW
    EXECUTE FUNCTION atendee.validate_professional_membership();

CREATE TABLE atendee.professional_services
(
    tenant_id UUID NOT NULL,
    professional_id UUID NOT NULL,
    service_id UUID NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    CONSTRAINT pk_professional_services PRIMARY KEY (tenant_id, professional_id, service_id),
    CONSTRAINT fk_professional_services_professional
        FOREIGN KEY (tenant_id, professional_id)
        REFERENCES atendee.professionals (tenant_id, professional_id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_professional_services_service
        FOREIGN KEY (tenant_id, service_id)
        REFERENCES atendee.services (tenant_id, service_id)
        ON DELETE RESTRICT
);

CREATE INDEX ix_professional_services_tenant_service
    ON atendee.professional_services (tenant_id, service_id, professional_id);

ALTER TABLE atendee.professional_services ENABLE ROW LEVEL SECURITY;
ALTER TABLE atendee.professional_services FORCE ROW LEVEL SECURITY;

CREATE POLICY professional_services_runtime_isolation
ON atendee.professional_services
FOR ALL
TO atendee_runtime
USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID)
WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::UUID);

CREATE FUNCTION atendee.audit_professional_change()
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
        RAISE EXCEPTION 'Trusted tenant actor context is required for professional mutations';
    END IF;

    IF TG_OP = 'INSERT' THEN
        audit_type := 'professional.created';
    ELSE
        audit_type := 'professional.updated';
    END IF;

    INSERT INTO atendee.tenant_audit_events
        (tenant_id, event_id, actor_identity_user_id, event_type, resource_id, related_resource_id, occurred_at_utc)
    VALUES
        (NEW.tenant_id, gen_random_uuid(), actor_text::uuid, audit_type, NEW.professional_id, NULL, clock_timestamp());
    RETURN NEW;
END;
$function$;

CREATE FUNCTION atendee.audit_professional_service_change()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, atendee
AS $function$
DECLARE
    actor_text text;
    audit_type text;
    target_tenant uuid;
    target_professional uuid;
    target_service uuid;
BEGIN
    actor_text := NULLIF(current_setting('app.actor_id', true), '');
    IF actor_text IS NULL THEN
        RAISE EXCEPTION 'Trusted tenant actor context is required for professional service assignment';
    END IF;

    IF TG_OP = 'INSERT' THEN
        audit_type := 'professional_service.assigned';
        target_tenant := NEW.tenant_id;
        target_professional := NEW.professional_id;
        target_service := NEW.service_id;
    ELSE
        audit_type := 'professional_service.unassigned';
        target_tenant := OLD.tenant_id;
        target_professional := OLD.professional_id;
        target_service := OLD.service_id;
    END IF;

    INSERT INTO atendee.tenant_audit_events
        (tenant_id, event_id, actor_identity_user_id, event_type, resource_id, related_resource_id, occurred_at_utc)
    VALUES
        (target_tenant, gen_random_uuid(), actor_text::uuid, audit_type,
         target_professional, target_service, clock_timestamp());

    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END;
$function$;

REVOKE ALL ON FUNCTION atendee.audit_professional_change() FROM PUBLIC;
REVOKE ALL ON FUNCTION atendee.audit_professional_service_change() FROM PUBLIC;

CREATE TRIGGER tr_professionals_audit
    AFTER INSERT OR UPDATE ON atendee.professionals
    FOR EACH ROW EXECUTE FUNCTION atendee.audit_professional_change();

CREATE TRIGGER tr_professional_services_audit
    AFTER INSERT OR DELETE ON atendee.professional_services
    FOR EACH ROW EXECUTE FUNCTION atendee.audit_professional_service_change();

GRANT SELECT, INSERT, UPDATE ON atendee.professionals TO atendee_runtime;
REVOKE DELETE, TRUNCATE ON atendee.professionals FROM atendee_runtime, PUBLIC;
GRANT SELECT, INSERT, DELETE ON atendee.professional_services TO atendee_runtime;
REVOKE UPDATE, TRUNCATE ON atendee.professional_services FROM atendee_runtime, PUBLIC;

RESET ROLE;
COMMIT;
